using System.Globalization;

namespace MovieNight.Library.Tests.Fixtures;

/// <summary>Nights to test with, and the DynamoDB items they are stored as.</summary>
internal static class TestNights
{
    public const string NightId = "night1";

    public const string SiteUrl = "https://movienight.example.com";

    /// <summary>
    /// The environment the library reads. Set once, before anything that reads it is built;
    /// every test class that builds a provider or service calls this from a static constructor.
    /// </summary>
    public static void InitializeEnvironment()
    {
        Environment.SetEnvironmentVariable("TABLE_NAME", "movienight-test");
        Environment.SetEnvironmentVariable("SITE_URL", SiteUrl);
        Environment.SetEnvironmentVariable("SENDER_ADDRESS", "donotreply@movienight.example.com");
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    /// <summary>The token for participant <paramref name="participantId"/>: its own id, which makes tests easy to read.</summary>
    public static string TokenFor(string participantId) => $"token-{participantId}";

    public static Participant Person(string id, string suggestion = "", bool isOrganizer = false, params string[] ranking) =>
        new()
        {
            ParticipantId = id,
            Email = $"{id}@example.com",
            TokenHashes = [SecretToken.Hash(TokenFor(id))],
            IsOrganizer = isOrganizer,
            Suggestion = suggestion,
            Ranking = [.. ranking]
        };

    public static Night Night(NightPhase phase, params Participant[] participants) =>
        new()
        {
            NightId = NightId,
            Date = new DateOnly(2026, 11, 14),
            Phase = phase,
            Participants = [.. participants]
        };

    /// <summary>The night as NightProvider stores it, for a substitute's QueryAsync to return.</summary>
    public static QueryResponse AsQueryResponse(Night night)
    {
        var partitionKey = $"NIGHT#{night.NightId}";

        var items = new List<Dictionary<string, AttributeValue>>
        {
            new()
            {
                ["PK"] = new() { S = partitionKey },
                ["SK"] = new() { S = "META" },
                ["Date"] = new() { S = night.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                ["Phase"] = new() { S = night.Phase.ToString() }
            }
        };

        items.AddRange(night.Participants.Select(participant =>
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = partitionKey },
                ["SK"] = new() { S = $"PARTICIPANT#{participant.ParticipantId}" },
                ["Email"] = new() { S = participant.Email },
                ["TokenHashes"] = new() { SS = [.. participant.TokenHashes] },
                ["IsOrganizer"] = new() { BOOL = participant.IsOrganizer }
            };

            if(participant.Suggestion.Length > 0)
                item["Suggestion"] = new() { S = participant.Suggestion };

            if(participant.Ranking.Count > 0)
                item["Ranking"] = new() { L = participant.Ranking.Select(id => new AttributeValue { S = id }).ToList() };

            return item;
        }));

        return new QueryResponse { Items = items };
    }

    public static TransactionCanceledException Cancelled(params string[] reasonCodes) =>
        new("cancelled")
        {
            CancellationReasons = reasonCodes.Select(code => new CancellationReason { Code = code }).ToList()
        };
}
