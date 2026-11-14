namespace MovieNight.Library.Providers;

/// <summary>
/// The inboxes that have asked never to be emailed again.
/// </summary>
/// <remarks>
/// Anybody can type anybody's address into the create form, so somebody who gets an invitation
/// from a stranger needs a way to make it stop for good, not just for that night.
///
/// An entry is the hash of the inbox, never the address: the list has to be kept forever, and a
/// list of addresses kept forever is a list somebody could want. It is keyed on the inbox rather
/// than the spelling, so asking once covers every +tag of the same address. Entries have no TTL.
/// </remarks>
[UsedImplicitly]
internal class DoNotEmailProvider
{
    private const string SortKey = "DONOTEMAIL";

    private readonly IAmazonDynamoDB _dynamoDbClient;

    private readonly string _tableName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public DoNotEmailProvider(IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _tableName = EnvReader.GetStringValue("TABLE_NAME");
    }

    public async Task<bool> IsListedAsync(string email)
    {
        var response = await _dynamoDbClient
            .GetItemAsync(new GetItemRequest
            {
                TableName = _tableName,
                Key = BuildKey(email),
                // Somebody who has just asked must not get the very next email.
                ConsistentRead = true
            })
            .ConfigureAwait(false);

        // The SDK leaves Item null, or empty, when there is no such item.
        return response.Item is { Count: > 0 };
    }

    /// <summary>Lists the address. Listing an address that is already listed changes nothing.</summary>
    public async Task AddAsync(string email) =>
        await _dynamoDbClient
            .PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = new Dictionary<string, AttributeValue>(BuildKey(email))
                {
                    ["ListedAt"] = new() { S = DateTimeOffset.UtcNow.ToString("O") }
                }
            })
            .ConfigureAwait(false);

    internal static string KeyFor(string email) => $"{SortKey}#{SecretToken.Hash(email.ToMailboxKey())}";

    private static Dictionary<string, AttributeValue> BuildKey(string email) =>
        new()
        {
            ["PK"] = new() { S = KeyFor(email) },
            ["SK"] = new() { S = SortKey }
        };
}
