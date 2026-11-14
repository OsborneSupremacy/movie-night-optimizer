using System.Globalization;

namespace MovieNight.Library.Providers;

/// <summary>
/// Storage for movie nights. One partition per night: a META item, and one item per participant
/// holding their suggestion and ranking, so a single Query reads the whole night.
/// </summary>
[UsedImplicitly]
internal class NightProvider
{
    private const string MetaSortKey = "META";

    private const string ParticipantSortKeyPrefix = "PARTICIPANT#";

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
                ["TokenHash"] = new() { S = participant.TokenHash },
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
                TokenHash = GetString(item, "TokenHash"),
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

    // TODO: SaveSuggestionAsync, SaveRankingAsync and AdvancePhaseAsync. Each should be a
    // conditional UpdateItem: the suggestion and ranking only while the META item is in the right
    // phase, and the phase change only from the phase it was read in, so that two people
    // finishing at the same moment cannot both send the next round of email.

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

    private static string BuildPartitionKey(string nightId) => $"NIGHT#{nightId}";

    private static string GetString(Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? value.S ?? string.Empty : string.Empty;
}
