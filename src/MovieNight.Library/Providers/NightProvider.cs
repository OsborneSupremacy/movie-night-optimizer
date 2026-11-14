using System.Globalization;

namespace MovieNight.Library.Providers;

/// <summary>
/// Storage for movie nights. One partition per night: a META item, and one item per participant
/// holding their suggestion and ranking, so a single Query reads the whole night.
/// </summary>
/// <remarks>
/// Every write after the first is conditional on the phase in the META item. A suggestion or a
/// ranking only lands while the night is in the phase that takes it, and the phase only moves on
/// from the phase it was read in, so two people finishing at the same moment cannot both send the
/// next round of email.
/// </remarks>
[UsedImplicitly]
internal class NightProvider
{
    /// <summary>The most nights one inbox can start in a UTC day.</summary>
    /// <remarks>
    /// Creating a night mails up to seven addresses a stranger typed, from an SES account other
    /// applications share. Three is plenty for a family that mistyped an address and started over.
    /// </remarks>
    internal const int DailyNightLimit = 3;

    /// <summary>How many times a transaction that lost a race with another is tried in all.</summary>
    internal const int MaxTransactionAttempts = 4;

    private const string MetaSortKey = "META";

    private const string ParticipantSortKeyPrefix = "PARTICIPANT#";

    private const string OrganizerDaySortKey = "ORGANIZERDAY";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>How long a night is kept. DynamoDB's TTL removes it some time after this.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly IAmazonDynamoDB _dynamoDbClient;

    private readonly string _tableName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public NightProvider(IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _tableName = EnvReader.GetStringValue("TABLE_NAME");
    }

    /// <summary>
    /// Counts one more night against the organizer's inbox for the UTC day, unless the day has had
    /// its fill.
    /// </summary>
    /// <remarks>
    /// One item per inbox per day, with the date in its key, so a new day is a new item and never
    /// needs resetting. The count and the check are one conditional update, so two requests racing
    /// cannot both take the last slot.
    /// </remarks>
    /// <param name="mailboxKey">The organizer's address as <see cref="StringExtensions.ToMailboxKey"/> gives it.</param>
    public async Task<bool> TryReserveOrganizerSlotAsync(string mailboxKey)
    {
        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;

        // A day past the day it counts, so an item is never reaped while it can still refuse.
        var expiresAt = new DateTimeOffset(today.AddDays(2), TimeSpan.Zero).ToUnixTimeSeconds();

        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"{OrganizerDaySortKey}#{mailboxKey}#{today.ToString(DateFormat, CultureInfo.InvariantCulture)}" },
                ["SK"] = new() { S = OrganizerDaySortKey }
            },
            UpdateExpression = "ADD NightCount :one SET #ttl = if_not_exists(#ttl, :ttl)",
            ConditionExpression = "attribute_not_exists(NightCount) OR NightCount < :limit",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#ttl"] = "ttl"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":one"] = new() { N = "1" },
                [":limit"] = new() { N = DailyNightLimit.ToString(CultureInfo.InvariantCulture) },
                [":ttl"] = new() { N = expiresAt.ToString(CultureInfo.InvariantCulture) }
            }
        };

        try
        {
            await _dynamoDbClient.UpdateItemAsync(request).ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a new night and its participants in one transaction, so a night never exists with
    /// only some of the people in it.
    /// </summary>
    public async Task SaveNewNightAsync(Night night)
    {
        var ttl = DateTimeOffset.UtcNow.Add(Retention).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var partitionKey = BuildPartitionKey(night.NightId);

        var items = new List<TransactWriteItem>
        {
            BuildPut(new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = partitionKey },
                ["SK"] = new() { S = MetaSortKey },
                ["Date"] = new() { S = night.Date.ToString(DateFormat, CultureInfo.InvariantCulture) },
                ["Phase"] = new() { S = night.Phase.ToString() },
                ["ttl"] = new() { N = ttl }
            })
        };

        items.AddRange(night.Participants.Select(participant =>
            BuildPut(new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = partitionKey },
                ["SK"] = new() { S = $"{ParticipantSortKeyPrefix}{participant.ParticipantId}" },
                ["Email"] = new() { S = participant.Email },
                ["TokenHashes"] = new() { SS = [.. participant.TokenHashes] },
                ["IsOrganizer"] = new() { BOOL = participant.IsOrganizer },
                ["ttl"] = new() { N = ttl }
            })));

        await _dynamoDbClient
            .TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = items })
            .ConfigureAwait(false);
    }

    public async Task<(bool found, Night night)> TryGetNightAsync(string nightId)
    {
        var response = await _dynamoDbClient
            .QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "PK = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new() { S = BuildPartitionKey(nightId) }
                },
                // The caller is usually about to decide whether everyone has answered, and an
                // eventually consistent read could miss the write that just completed the set.
                ConsistentRead = true
            })
            .ConfigureAwait(false);

        // The SDK leaves a collection null rather than empty when the service returns none.
        var items = response.Items ?? [];

        var meta = items.FirstOrDefault(item => GetString(item, "SK") == MetaSortKey);

        if(meta is null)
            return (false, Night.Empty);

        var participants = items
            .Where(item => GetString(item, "SK").StartsWith(ParticipantSortKeyPrefix, StringComparison.Ordinal))
            .Select(item => new Participant
            {
                ParticipantId = GetString(item, "SK")[ParticipantSortKeyPrefix.Length..],
                Email = GetString(item, "Email"),
                TokenHashes = item.TryGetValue("TokenHashes", out var tokenHashes) && tokenHashes.SS is not null
                    ? [.. tokenHashes.SS]
                    : [],
                IsOrganizer = item.TryGetValue("IsOrganizer", out var isOrganizer) && isOrganizer.BOOL == true,
                Suggestion = GetString(item, "Suggestion"),
                Ranking = item.TryGetValue("Ranking", out var ranking) && ranking.L is not null
                    ? ranking.L.Select(value => value.S ?? string.Empty).ToImmutableList()
                    : []
            })
            .ToImmutableList();

        var night = new Night
        {
            NightId = nightId,
            Date = DateOnly.ParseExact(GetString(meta, "Date"), DateFormat, CultureInfo.InvariantCulture),
            Phase = Enum.Parse<NightPhase>(GetString(meta, "Phase")),
            Participants = participants
        };

        return (true, night);
    }

    /// <summary>
    /// Records a participant's movie, replacing any they suggested before. False if the night has
    /// stopped collecting, or the participant isn't in it.
    /// </summary>
    public Task<bool> SaveSuggestionAsync(string nightId, string participantId, string suggestion) =>
        UpdateParticipantInPhaseAsync(
            nightId,
            participantId,
            NightPhase.Collecting,
            "SET Suggestion = :value",
            new AttributeValue { S = suggestion }
        );

    /// <summary>
    /// Records a participant's ranking, best first, replacing any they gave before. False if the
    /// night isn't ranking, or the participant isn't in it.
    /// </summary>
    public Task<bool> SaveRankingAsync(string nightId, string participantId, ImmutableList<string> ranking) =>
        UpdateParticipantInPhaseAsync(
            nightId,
            participantId,
            NightPhase.Ranking,
            "SET Ranking = :value",
            new AttributeValue { L = ranking.Select(movieId => new AttributeValue { S = movieId }).ToList() }
        );

    /// <summary>
    /// Moves the night from <paramref name="from"/> to <paramref name="to"/> and adds each
    /// participant's new token hash, all or nothing. False if the night had already left
    /// <paramref name="from"/>, which means somebody else moved it and is sending the email.
    /// </summary>
    /// <param name="newTokenHashes">By participant id: the hash of the token in the email about to be sent.</param>
    public Task<bool> AdvancePhaseAsync(
        string nightId,
        NightPhase from,
        NightPhase to,
        ImmutableDictionary<string, string> newTokenHashes
    )
    {
        var items = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = _tableName,
                    Key = BuildKey(nightId, MetaSortKey),
                    UpdateExpression = "SET Phase = :to",
                    ConditionExpression = "Phase = :from",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":from"] = new() { S = from.ToString() },
                        [":to"] = new() { S = to.ToString() }
                    }
                }
            }
        };

        items.AddRange(newTokenHashes
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new TransactWriteItem
            {
                Update = new Update
                {
                    TableName = _tableName,
                    Key = BuildKey(nightId, $"{ParticipantSortKeyPrefix}{entry.Key}"),
                    UpdateExpression = "ADD TokenHashes :hash",
                    ConditionExpression = "attribute_exists(PK)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":hash"] = new() { SS = [entry.Value] }
                    }
                }
            }));

        return TryTransactWriteAsync(new TransactWriteItemsRequest { TransactItems = items });
    }

    /// <summary>
    /// Updates a participant's item only while the night is in <paramref name="phase"/>. The phase
    /// is a condition check on the META item in the same transaction, so a write can't slip in after
    /// the round has closed and change a result that has already been sent.
    /// </summary>
    private Task<bool> UpdateParticipantInPhaseAsync(
        string nightId,
        string participantId,
        NightPhase phase,
        string updateExpression,
        AttributeValue value
    ) =>
        TryTransactWriteAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    ConditionCheck = new ConditionCheck
                    {
                        TableName = _tableName,
                        Key = BuildKey(nightId, MetaSortKey),
                        ConditionExpression = "Phase = :phase",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":phase"] = new() { S = phase.ToString() }
                        }
                    }
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = _tableName,
                        Key = BuildKey(nightId, $"{ParticipantSortKeyPrefix}{participantId}"),
                        UpdateExpression = updateExpression,
                        ConditionExpression = "attribute_exists(PK)",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":value"] = value
                        }
                    }
                }
            ]
        });

    /// <summary>
    /// True if the transaction was written, false if one of its conditions didn't hold.
    /// </summary>
    /// <remarks>
    /// Every write in a night touches its META item, so two people answering at the same moment
    /// can cancel each other with a TransactionConflict even though both writes are fine. That is
    /// retried, briefly; a failed condition is an answer and is not.
    /// </remarks>
    private async Task<bool> TryTransactWriteAsync(TransactWriteItemsRequest request)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _dynamoDbClient.TransactWriteItemsAsync(request).ConfigureAwait(false);
                return true;
            }
            catch (TransactionCanceledException exception)
            {
                var reasons = exception.CancellationReasons ?? [];

                var lostARace = reasons.Any(reason => reason.Code == "TransactionConflict")
                    && !reasons.Any(reason => reason.Code == "ConditionalCheckFailed");

                if(!lostARace || attempt >= MaxTransactionAttempts)
                    return false;

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt * attempt)).ConfigureAwait(false);
            }
        }
    }

    private TransactWriteItem BuildPut(Dictionary<string, AttributeValue> item) =>
        new()
        {
            Put = new Put
            {
                TableName = _tableName,
                Item = item,
                ConditionExpression = "attribute_not_exists(PK)"
            }
        };

    private static Dictionary<string, AttributeValue> BuildKey(string nightId, string sortKey) =>
        new()
        {
            ["PK"] = new() { S = BuildPartitionKey(nightId) },
            ["SK"] = new() { S = sortKey }
        };

    private static string BuildPartitionKey(string nightId) => $"NIGHT#{nightId}";

    private static string GetString(Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? value.S ?? string.Empty : string.Empty;
}
