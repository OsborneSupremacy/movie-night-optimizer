using static MovieNight.Library.Tests.Fixtures.TestNights;

namespace MovieNight.Library.Tests.ServiceTests;

/// <summary>
/// The endpoints, run for real over substitute DynamoDB and SES clients.
/// </summary>
public class NightServicesTests
{
    static NightServicesTests() => InitializeEnvironment();

    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly ApiGatewayAdapter _adapter;

    private readonly NightProvider _nightProvider;

    private readonly NightReader _nightReader;

    private readonly PhaseAdvancer _phaseAdvancer;

    private readonly EmailSender _emailSender;

    private readonly DoNotEmailProvider _doNotEmail;

    public NightServicesTests()
    {
        _adapter = new ApiGatewayAdapter(
            NullLogger<ApiGatewayAdapter>.Instance,
            new System.Text.Json.JsonSerializerOptions(),
            new ServiceCollection().BuildServiceProvider()
        );
        _nightProvider = new NightProvider(_dynamoDb);
        _nightReader = new NightReader(_nightProvider);
        _doNotEmail = new DoNotEmailProvider(_dynamoDb);
        _emailSender = new EmailSender(_ses, _doNotEmail, NullLogger<EmailSender>.Instance);

        // Nobody is on the do-not-email list unless a test says so. A substitute would otherwise
        // answer GetItem with null, which the real client never does.
        _dynamoDb.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse());
        _phaseAdvancer = new PhaseAdvancer(_nightProvider, _emailSender, NullLogger<PhaseAdvancer>.Instance);
    }

    private static readonly CreateNightRequest CreateRequest = new()
    {
        OrganizerEmail = "Organizer@example.com",
        ParticipantEmails = ["one@example.com", "two@example.com"],
        Date = "2026-11-14"
    };

    [Fact]
    public async Task CreateNight_OverTheDailyLimit_Is429AndSavesAndSendsNothing()
    {
        _dynamoDb.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("full"));

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        await _dynamoDb.DidNotReceiveWithAnyArgs().TransactWriteItemsAsync(default!, default);
        await _ses.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task CreateNight_SavesEveryoneAndEmailsEachALinkThatOpensTheirOwnPlace()
    {
        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<TransactWriteItemsRequest>()
            .Single()
            .TransactItems
            .Select(item => item.Put.Item)
            .Where(item => item["SK"].S.StartsWith("PARTICIPANT#"))
            .ToList();

        saved.Should().HaveCount(3);
        saved.Count(item => item["IsOrganizer"].BOOL == true).Should().Be(1);

        var sent = SentEmails();
        sent.Should().HaveCount(3);

        foreach (var email in sent)
        {
            // The link's token hashes to the stored hash of the person it was sent to, and the
            // token itself was never stored.
            var token = TokenIn(email.Message.Body.Text.Data);
            var to = email.Destination.ToAddresses.Single();
            var item = saved.Single(candidate => candidate["Email"].S == to);

            item["TokenHashes"].SS.Should().Equal(SecretToken.Hash(token));
            saved.SelectMany(candidate => candidate.Values).Select(value => value.S).Should().NotContain(token);
        }
    }

    [Fact]
    public async Task CreateNight_KeysTheLimitOnTheOrganizersInbox()
    {
        await CreateNightService().ExecuteAsync(CreateRequest with { OrganizerEmail = "Organizer+movies@Example.com" });

        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<UpdateItemRequest>()
            .First()
            .Key["PK"].S.Should().StartWith("ORGANIZERDAY#organizer@example.com#");
    }

    [Fact]
    public async Task CreateNight_TheApplicationsDayIsFull_Is429AndSavesAndSendsNothing()
    {
        // The organizer's slot is free; the application-wide one is not.
        _dynamoDb.UpdateItemAsync(
                Arg.Is<UpdateItemRequest>(request => request.Key["SK"].S == "APPDAY"),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("full"));

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        result.Exception.Message.Should().Be(MovieNight.Library.Services.CreateNightService.ApplicationLimitMessage);
        await _dynamoDb.DidNotReceiveWithAnyArgs().TransactWriteItemsAsync(default!, default);
        await _ses.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task CreateNight_OrganizerHasOptedOut_Is409AndSpendsNoSlot()
    {
        GivenListed("organizer@example.com");

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await _dynamoDb.DidNotReceiveWithAnyArgs().UpdateItemAsync(default(UpdateItemRequest)!, default);
        await _ses.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task CreateNight_AGuestHasOptedOut_LeavesThemOutOfTheNightAndTheMail()
    {
        GivenListed("two@example.com");

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        result.Value.OptedOutCount.Should().Be(1);
        result.Value.InvitedCount.Should().Be(2);

        var stored = Transactions().Single().TransactItems
            .Select(item => item.Put.Item)
            .Where(item => item["SK"].S.StartsWith("PARTICIPANT#"))
            .Select(item => item["Email"].S);
        stored.Should().BeEquivalentTo(["Organizer@example.com", "one@example.com"]);
        SentEmails().SelectMany(email => email.Destination.ToAddresses).Should().NotContain("two@example.com");
    }

    [Fact]
    public async Task CreateNight_EveryGuestHasOptedOut_Is409()
    {
        GivenListed("one@example.com");
        GivenListed("two@example.com");

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateNight_EveryEmailCarriesAStopLinkWithTheRecipientsOwnToken()
    {
        await CreateNightService().ExecuteAsync(CreateRequest);

        foreach (var email in SentEmails())
        {
            var text = email.Message.Body.Text.Data;
            text.Should().Contain($"/night/");
            text.Should().Contain("/stop#t=" + TokenIn(text));
            email.Message.Body.Html.Data.Should().Contain("/stop#t=" + TokenIn(text));
        }
    }

    [Fact]
    public async Task EmailSender_ListedAddress_IsNeverSent()
    {
        GivenListed("one@example.com");

        await _emailSender.SendAsync("ONE+movies@example.com", "subject", "<p>html</p>", "text");

        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task OptOut_ListsTheInboxAndMarksThemOutOfTheNight()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"), Person("c")));

        var result = await OptOutService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("b") });

        result.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listed = _dynamoDb.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<PutItemRequest>().Single();
        listed.Item["PK"].S.Should().Be(DoNotEmailProvider.KeyFor("b@example.com"));
        listed.Item.Values.Select(value => value.S).Should().NotContain("b@example.com");

        var marked = _dynamoDb.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<UpdateItemRequest>().Single();
        marked.Key["SK"].S.Should().Be("PARTICIPANT#b");
        marked.UpdateExpression.Should().Be("SET OptedOut = :true");
    }

    [Fact]
    public async Task OptOut_TheLastPersonTheRoundWaitedOn_MovesTheNightOn()
    {
        var before = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b", "Big"), Person("c"));
        var after = before with { Participants = [Person("a", "Alien", isOrganizer: true), Person("b", "Big"), Person("c") with { OptedOut = true }] };
        GivenTheNightReads(before, after, after with { Phase = NightPhase.Ranking });
        GivenListed("c@example.com");

        var result = await OptOutService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("c") });

        result.StatusCode.Should().Be(HttpStatusCode.NoContent, result.IsFaulted ? result.Exception.ToString() : "");
        Transactions().Single().TransactItems[0].Update.ExpressionAttributeValues[":to"].S.Should().Be("Ranking");
        SentEmails().SelectMany(email => email.Destination.ToAddresses).Should().BeEquivalentTo(["a@example.com", "b@example.com"]);
    }

    [Fact]
    public async Task OptOut_UnknownToken_Is404AndListsNobody()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a")));

        var result = await OptOutService().ExecuteAsync(new NightRequest { NightId = NightId, Token = "wrong" });

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _dynamoDb.DidNotReceiveWithAnyArgs().PutItemAsync(default(PutItemRequest)!, default);
    }

    [Fact]
    public async Task GetNight_UnknownToken_Is404()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a")));

        var result = await new GetNightService(_adapter, _nightReader)
            .ExecuteAsync(new NightRequest { NightId = NightId, Token = "wrong" });

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubmitSuggestion_NotTheLastOne_DoesNotAdvance()
    {
        var before = Night(NightPhase.Collecting, Person("a"), Person("b"), Person("c"));
        GivenTheNightReads(before, before with { Participants = [Person("a", "Alien"), Person("b"), Person("c")] });

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("a", "Alien"));

        result.IsSuccess.Should().BeTrue();
        Transactions().Should().HaveCount(1);
        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitSuggestion_TheLastOne_MovesToRankingAndEmailsEveryoneANewWorkingLink()
    {
        var before = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b", "Big"), Person("c"));
        var complete = before with { Participants = [Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars")] };
        GivenTheNightReads(before, complete, complete with { Phase = NightPhase.Ranking });

        await SubmitSuggestionService().ExecuteAsync(Suggestion("c", "Cars"));

        var advance = Transactions()[1];
        advance.TransactItems[0].Update.ExpressionAttributeValues[":to"].S.Should().Be("Ranking");

        var addedHashes = advance.TransactItems.Skip(1)
            .Select(item => item.Update.ExpressionAttributeValues[":hash"].SS.Single())
            .ToList();

        var sent = SentEmails();
        sent.Should().HaveCount(3);
        sent.Should().OnlyContain(email => email.Message.Subject.Data.StartsWith("Rank the movies"));
        sent.Select(email => SecretToken.Hash(TokenIn(email.Message.Body.Text.Data)))
            .Should().BeEquivalentTo(addedHashes);
    }

    [Fact]
    public async Task SubmitSuggestion_SomebodyElsesMovie_Is409AndSavesNothing()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", "The Princess Bride"), Person("b")));

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("b", "princess bride"));

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        result.Exception.Message.Should().Be(MovieNight.Library.Services.SubmitSuggestionService.TakenMessage);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitSuggestion_AfterCollectingClosed_Is409()
    {
        GivenTheNightReads(Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big")));

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("a", "Aliens"));

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitSuggestion_SomebodyElseAdvancedFirst_SendsNoMail()
    {
        // Two people finishing at once: both see a complete round, only one phase change lands.
        var before = Night(NightPhase.Collecting, Person("a", "Alien"), Person("b"));
        var complete = before with { Participants = [Person("a", "Alien"), Person("b", "Big")] };
        GivenTheNightReads(before, complete, complete with { Phase = NightPhase.Ranking });

        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new TransactWriteItemsResponse()),
                _ => throw Cancelled("ConditionalCheckFailed", "None", "None")
            );

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("b", "Big"));

        result.IsSuccess.Should().BeTrue();
        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitRanking_IncludingTheirOwnMovie_Is400()
    {
        GivenTheNightReads(Night(NightPhase.Ranking, Person("a", "Alien"), Person("b", "Big"), Person("c", "Cars")));

        var result = await SubmitRankingService().ExecuteAsync(Ranking("a", "a", "b"));

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitRanking_TheLastOne_EmailsEveryoneTheResults()
    {
        var before = Night(NightPhase.Ranking,
            Person("a", "Alien", false, "b"),
            Person("b", "Big"));
        var complete = before with { Participants = [Person("a", "Alien", false, "b"), Person("b", "Big", false, "a")] };
        GivenTheNightReads(before, complete, complete with { Phase = NightPhase.Done });

        await SubmitRankingService().ExecuteAsync(Ranking("b", "a"));

        SentEmails().Should().HaveCount(2)
            .And.OnlyContain(email => email.Message.Subject.Data.StartsWith("Movie night"));
    }

    [Fact]
    public async Task Advance_NotTheOrganizer_Is403()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b", "Big")));

        var result = await AdvanceNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("b") });

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task Advance_NobodyHasSuggested_Is409()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", isOrganizer: true), Person("b")));

        var result = await AdvanceNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Advance_OrganizerClosesCollectingWithOneSuggestion_GoesStraightToResults()
    {
        var before = Night(NightPhase.Collecting, Person("a", "Alien", isOrganizer: true), Person("b"));
        GivenTheNightReads(before, before with { Phase = NightPhase.Done });

        var result = await AdvanceNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        result.IsSuccess.Should().BeTrue();
        Transactions().Single().TransactItems[0].Update.ExpressionAttributeValues[":to"].S.Should().Be("Done");
        SentEmails().Should().HaveCount(2);
    }

    private CreateNightService CreateNightService() => new(_adapter, _nightProvider, _emailSender, _doNotEmail);

    private OptOutService OptOutService() => new(_adapter, _nightReader, _nightProvider, _doNotEmail, _phaseAdvancer);

    private void GivenListed(string email) =>
        _dynamoDb.GetItemAsync(
                Arg.Is<GetItemRequest>(request => request.Key["PK"].S == DoNotEmailProvider.KeyFor(email)),
                Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["PK"] = new() { S = DoNotEmailProvider.KeyFor(email) } }
            });

    private SubmitSuggestionService SubmitSuggestionService() => new(_adapter, _nightReader, _nightProvider, _phaseAdvancer);

    private SubmitRankingService SubmitRankingService() => new(_adapter, _nightReader, _nightProvider, _phaseAdvancer);

    private AdvanceNightService AdvanceNightService() => new(_adapter, _nightReader, _phaseAdvancer);

    private static SubmitSuggestionRequest Suggestion(string participantId, string title) =>
        new() { NightId = NightId, Token = TokenFor(participantId), Suggestion = title };

    private static SubmitRankingRequest Ranking(string participantId, params string[] ranking) =>
        new() { NightId = NightId, Token = TokenFor(participantId), Ranking = [.. ranking] };

    /// <summary>Each read of the night returns the next of these, and the last one from then on.</summary>
    private void GivenTheNightReads(Night first, params Night[] then) =>
        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsQueryResponse(first), then.Select(AsQueryResponse).ToArray());

    private List<TransactWriteItemsRequest> Transactions() =>
        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<TransactWriteItemsRequest>()
            .ToList();

    private List<SendEmailRequest> SentEmails() =>
        _ses.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<SendEmailRequest>()
            .ToList();

    private static string TokenIn(string text)
    {
        var start = text.IndexOf("#t=", StringComparison.Ordinal) + 3;
        var end = text.IndexOfAny([' ', '\n'], start);
        return text[start..(end < 0 ? text.Length : end)];
    }
}
