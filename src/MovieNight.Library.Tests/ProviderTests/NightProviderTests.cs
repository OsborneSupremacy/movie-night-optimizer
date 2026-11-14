using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ProviderTests;

/// <summary>
/// The conditions on each write, which are what stop a late answer from changing a sent result
/// and two finishers from both sending the next round of email.
/// </summary>
public class NightProviderTests
{
    static NightProviderTests() => InitializeEnvironment();

    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();

    private readonly NightProvider _sut;

    public NightProviderTests() => _sut = new NightProvider(_dynamoDb);

    [Fact]
    public async Task SaveSuggestionAsync_ChecksTheNightIsStillCollecting()
    {
        await _sut.SaveSuggestionAsync(NightId, "a", "Alien");

        var items = CapturedTransaction().TransactItems;

        items[0].ConditionCheck.Key["SK"].S.Should().Be("META");
        items[0].ConditionCheck.ConditionExpression.Should().Be("Phase = :phase");
        items[0].ConditionCheck.ExpressionAttributeValues[":phase"].S.Should().Be("Collecting");
        items[1].Update.Key["SK"].S.Should().Be("PARTICIPANT#a");
        items[1].Update.ConditionExpression.Should().Be("attribute_exists(PK)");
        items[1].Update.ExpressionAttributeValues[":value"].S.Should().Be("Alien");
    }

    [Fact]
    public async Task SaveRankingAsync_ChecksTheNightIsRankingAndStoresTheOrder()
    {
        await _sut.SaveRankingAsync(NightId, "a", ["c", "b"]);

        var items = CapturedTransaction().TransactItems;

        items[0].ConditionCheck.ExpressionAttributeValues[":phase"].S.Should().Be("Ranking");
        items[1].Update.ExpressionAttributeValues[":value"].L.Select(value => value.S).Should().Equal("c", "b");
    }

    [Fact]
    public async Task SaveSuggestionAsync_WrongPhase_ReturnsFalseWithoutRetrying()
    {
        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Cancelled("ConditionalCheckFailed", "None"));

        var saved = await _sut.SaveSuggestionAsync(NightId, "a", "Alien");

        saved.Should().BeFalse();
        await _dynamoDb.Received(1).TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSuggestionAsync_LosesARaceOnce_RetriesAndSaves()
    {
        // Two people answering at once both touch the META item, and one is cancelled for it.
        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw Cancelled("TransactionConflict", "None"),
                _ => Task.FromResult(new TransactWriteItemsResponse())
            );

        var saved = await _sut.SaveSuggestionAsync(NightId, "a", "Alien");

        saved.Should().BeTrue();
        await _dynamoDb.Received(2).TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSuggestionAsync_KeepsLosingRaces_GivesUpAfterTheLastAttempt()
    {
        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Cancelled("TransactionConflict", "None"));

        var saved = await _sut.SaveSuggestionAsync(NightId, "a", "Alien");

        saved.Should().BeFalse();
        await _dynamoDb.Received(NightProvider.MaxTransactionAttempts)
            .TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvancePhaseAsync_IsConditionalOnThePhaseItWasReadIn()
    {
        await _sut.AdvancePhaseAsync(NightId, NightPhase.Collecting, NightPhase.Ranking,
            ImmutableDictionary<string, string>.Empty.Add("a", "hash-a").Add("b", "hash-b"));

        var items = CapturedTransaction().TransactItems;

        items[0].Update.ConditionExpression.Should().Be("Phase = :from");
        items[0].Update.ExpressionAttributeValues[":from"].S.Should().Be("Collecting");
        items[0].Update.ExpressionAttributeValues[":to"].S.Should().Be("Ranking");
        items.Skip(1).Select(item => item.Update.ExpressionAttributeValues[":hash"].SS.Single())
            .Should().Equal("hash-a", "hash-b");
    }

    [Fact]
    public async Task AdvancePhaseAsync_SomebodyElseAdvancedFirst_ReturnsFalse()
    {
        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Cancelled("ConditionalCheckFailed", "None", "None"));

        var advanced = await _sut.AdvancePhaseAsync(NightId, NightPhase.Collecting, NightPhase.Ranking,
            ImmutableDictionary<string, string>.Empty.Add("a", "hash-a"));

        advanced.Should().BeFalse();
    }

    [Fact]
    public async Task TryReserveOrganizerSlotAsync_CountsAgainstTheDayOnlyUpToItsLimit()
    {
        await _sut.TryReserveOrganizerSlotAsync("ben@example.com");

        var update = _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<UpdateItemRequest>()
            .Single();

        update.Key["PK"].S.Should().Be($"ORGANIZERDAY#ben@example.com#{DateTimeOffset.UtcNow.UtcDateTime:yyyy-MM-dd}");
        update.ConditionExpression.Should().Be("attribute_not_exists(NightCount) OR NightCount < :limit");
        update.ExpressionAttributeValues[":limit"].N.Should().Be(NightProvider.DailyNightLimit.ToString());
    }

    [Fact]
    public async Task TryReserveOrganizerSlotAsync_DayHasHadItsFill_Refuses()
    {
        _dynamoDb.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("full"));

        (await _sut.TryReserveOrganizerSlotAsync("ben@example.com")).Should().BeFalse();
    }

    [Fact]
    public async Task TryGetNightAsync_ReadsBackWhatIsStored()
    {
        var stored = Night(NightPhase.Ranking,
            Person("a", "Alien", isOrganizer: true),
            Person("b", "Big", false, "a"));

        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsQueryResponse(stored));

        var (found, night) = await _sut.TryGetNightAsync(NightId);

        found.Should().BeTrue();
        night.Should().BeEquivalentTo(stored);
    }

    [Fact]
    public async Task TryGetNightAsync_NoItems_IsNotFound()
    {
        // The SDK leaves Items null rather than empty.
        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse());

        (await _sut.TryGetNightAsync(NightId)).found.Should().BeFalse();
    }

    private TransactWriteItemsRequest CapturedTransaction() =>
        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<TransactWriteItemsRequest>()
            .Last();
}
