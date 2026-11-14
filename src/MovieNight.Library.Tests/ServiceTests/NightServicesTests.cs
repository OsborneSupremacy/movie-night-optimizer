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

    private readonly IMovieLookup _movieLookup = Substitute.For<IMovieLookup>();

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

        // TMDB finds nothing unless a test says otherwise.
        _movieLookup.FindAsync(Arg.Any<string>()).Returns(MovieMatch.None);
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
    public async Task CreateNight_SavesEveryonePendingAndEmailsOnlyTheOrganizerToConfirm()
    {
        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.Created);

        var puts = Transactions().Single().TransactItems.Select(item => item.Put.Item).ToList();
        puts.Single(item => item["SK"].S == "META")["Phase"].S.Should().Be("Pending");

        var saved = puts.Where(item => item["SK"].S.StartsWith("PARTICIPANT#")).ToList();
        saved.Should().HaveCount(3);

        // One email, to the organizer, and nobody else hears a thing until they confirm.
        var sent = SentEmails().Single();
        sent.Destination.ToAddresses.Should().Equal("Organizer@example.com");
        sent.Message.Subject.Data.Should().StartWith("Confirm your movie night");

        // Its link opens the organizer's own place in the night, and the token was never stored.
        var token = TokenIn(sent.Message.Body.Text.Data);
        var organizer = saved.Single(item => item["IsOrganizer"].BOOL == true);
        organizer["TokenHashes"].SS.Should().Equal(SecretToken.Hash(token));
        saved.SelectMany(item => item.Values).Select(value => value.S).Should().NotContain(token);
    }

    [Fact]
    public async Task CreateNight_TheConfirmationEmailNamesNoGuest()
    {
        // Whoever typed the organizer address may not own it, and its owner shouldn't learn who
        // they meant to invite.
        await CreateNightService().ExecuteAsync(CreateRequest);

        var body = SentEmails().Single().Message.Body;
        body.Text.Data.Should().NotContain("one@example.com").And.NotContain("two@example.com");
        body.Html.Data.Should().NotContain("one@example.com").And.NotContain("two@example.com");
    }

    [Fact]
    public async Task CreateNight_TheConfirmationEmailFails_Is502()
    {
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSimpleEmailServiceException("nope"));

        var result = await CreateNightService().ExecuteAsync(CreateRequest);

        result.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Confirm_ByTheOrganizer_StartsTheNightAndInvitesOnlyTheGuests()
    {
        var pending = Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b"), Person("c"));
        GivenTheNightReads(pending, pending with { Phase = NightPhase.Collecting });

        var result = await ConfirmNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        result.IsSuccess.Should().BeTrue();

        var advance = Transactions().Single();
        advance.TransactItems[0].Update.ExpressionAttributeValues[":from"].S.Should().Be("Pending");
        advance.TransactItems[0].Update.ExpressionAttributeValues[":to"].S.Should().Be("Collecting");

        var sent = SentEmails();
        sent.SelectMany(email => email.Destination.ToAddresses).Should().BeEquivalentTo(["b@example.com", "c@example.com"]);
        sent.Should().OnlyContain(email => email.Message.Subject.Data.StartsWith("Pick a movie"));
        sent.Should().OnlyContain(email => email.Message.Body.Text.Data.Contains("a@example.com added your address"));

        // Each invitation carries a new token whose hash went in with the phase change.
        var addedHashes = advance.TransactItems.Skip(1).Select(item => item.Update.ExpressionAttributeValues[":hash"].SS.Single());
        sent.Select(email => SecretToken.Hash(TokenIn(email.Message.Body.Text.Data))).Should().BeSubsetOf(addedHashes);
    }

    [Fact]
    public async Task Confirm_ByAGuest_Is403AndSendsNothing()
    {
        GivenTheNightReads(Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b")));

        var result = await ConfirmNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("b") });

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Transactions().Should().BeEmpty();
        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_AgainOnANightUnderWay_ChangesAndSendsNothing()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", isOrganizer: true), Person("b")));

        var result = await ConfirmNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        result.IsSuccess.Should().BeTrue();
        result.Value.Phase.Should().Be("collecting");
        Transactions().Should().BeEmpty();
        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_TwoClicksAtOnce_InviteOnce()
    {
        // The second click finds the phase already moved, and its conditional write fails.
        var pending = Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b"));
        GivenTheNightReads(pending);
        _dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Cancelled("ConditionalCheckFailed", "None", "None"));

        await ConfirmNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        SentEmails().Should().BeEmpty();
    }

    [Fact]
    public async Task Advance_APendingNight_Is409()
    {
        GivenTheNightReads(Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b")));

        var result = await AdvanceNightService().ExecuteAsync(new NightRequest { NightId = NightId, Token = TokenFor("a") });

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitSuggestion_BeforeTheNightIsConfirmed_Is409()
    {
        GivenTheNightReads(Night(NightPhase.Pending, Person("a", isOrganizer: true), Person("b")));

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("a", "Alien"));

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
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

    private static readonly MovieMatch PrincessBride = new()
    {
        TmdbId = 2493, Title = "The Princess Bride", Year = "1987", PosterPath = "/bride.jpg"
    };

    [Fact]
    public async Task SubmitSuggestion_TmdbFindsIt_SavesTmdbsTitleAndDetails()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a"), Person("b")));
        _movieLookup.FindAsync("princess bride").Returns(PrincessBride);

        await SubmitSuggestionService().ExecuteAsync(Suggestion("a", "princess bride"));

        var values = Transactions().Single().TransactItems[1].Update.ExpressionAttributeValues;
        values[":title"].S.Should().Be("The Princess Bride");
        values[":tmdbId"].N.Should().Be("2493");
        values[":year"].S.Should().Be("1987");
        values[":poster"].S.Should().Be("/bride.jpg");
    }

    [Fact]
    public async Task SubmitSuggestion_TmdbFindsNothingOrIsDown_SavesWhatWasTyped()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a"), Person("b")));

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("a", "Grandpa's home videos"));

        result.IsSuccess.Should().BeTrue();
        var values = Transactions().Single().TransactItems[1].Update.ExpressionAttributeValues;
        values[":title"].S.Should().Be("Grandpa's home videos");
        values[":tmdbId"].N.Should().Be("0");
    }

    [Fact]
    public async Task SubmitSuggestion_TheSameTmdbMovieTypedDifferently_Is409()
    {
        var taken = Person("a", "Paddington 2") with { TmdbId = 346648, MovieYear = "2017" };
        GivenTheNightReads(Night(NightPhase.Collecting, taken, Person("b")));
        _movieLookup.FindAsync("Paddington Two").Returns(new MovieMatch
        {
            TmdbId = 346648, Title = "Paddington 2", Year = "2017", PosterPath = ""
        });

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("b", "Paddington Two"));

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Transactions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitSuggestion_ADifferentFilmWithTheSameTitle_IsAllowed()
    {
        var dune2021 = Person("a", "Dune") with { TmdbId = 438631, MovieYear = "2021" };
        GivenTheNightReads(Night(NightPhase.Collecting, dune2021, Person("b")));
        _movieLookup.FindAsync("Dune 1984").Returns(new MovieMatch
        {
            TmdbId = 841, Title = "Dune", Year = "1984", PosterPath = ""
        });

        var result = await SubmitSuggestionService().ExecuteAsync(Suggestion("b", "Dune 1984"));

        result.IsSuccess.Should().BeTrue();
        Transactions().Single().TransactItems[1].Update.ExpressionAttributeValues[":tmdbId"].N.Should().Be("841");
    }

    private static SubmitSuggestionRequest Picked(string participantId, string title, int tmdbId) =>
        Suggestion(participantId, title) with { TmdbId = tmdbId };

    [Fact]
    public async Task SubmitSuggestion_PickedFromTheList_IsFetchedByIdNotSearched()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a"), Person("b")));
        _movieLookup.FindByIdAsync(841).Returns(new MovieMatch { TmdbId = 841, Title = "Dune", Year = "1984", PosterPath = "/d.jpg" });

        await SubmitSuggestionService().ExecuteAsync(Picked("a", "Dune", 841));

        await _movieLookup.DidNotReceiveWithAnyArgs().FindAsync(default!);
        var values = Transactions().Single().TransactItems[1].Update.ExpressionAttributeValues;
        values[":tmdbId"].N.Should().Be("841");
        values[":year"].S.Should().Be("1984");
    }

    [Fact]
    public async Task SubmitSuggestion_PickingADifferentFilmWithATakenTitle_IsAllowed()
    {
        // "Dune" is taken as the 2021 film; the 1984 one picked from the list is a different movie.
        var dune2021 = Person("a", "Dune") with { TmdbId = 438631, MovieYear = "2021" };
        GivenTheNightReads(Night(NightPhase.Collecting, dune2021, Person("b")));
        _movieLookup.FindByIdAsync(841).Returns(new MovieMatch { TmdbId = 841, Title = "Dune", Year = "1984", PosterPath = "" });

        var result = await SubmitSuggestionService().ExecuteAsync(Picked("b", "Dune", 841));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitSuggestion_PickingTheSameFilm_Is409()
    {
        var taken = Person("a", "Dune") with { TmdbId = 438631, MovieYear = "2021" };
        GivenTheNightReads(Night(NightPhase.Collecting, taken, Person("b")));
        _movieLookup.FindByIdAsync(438631).Returns(new MovieMatch { TmdbId = 438631, Title = "Dune", Year = "2021", PosterPath = "" });

        var result = await SubmitSuggestionService().ExecuteAsync(Picked("b", "Dune", 438631));

        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SubmitSuggestion_APickTmdbCantFetch_IsSavedAsTheTitleWithoutSearchingAgain()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a"), Person("b")));
        _movieLookup.FindByIdAsync(Arg.Any<int>()).Returns(MovieMatch.None);

        var result = await SubmitSuggestionService().ExecuteAsync(Picked("a", "Spirited Away", 129));

        result.IsSuccess.Should().BeTrue();
        await _movieLookup.DidNotReceiveWithAnyArgs().FindAsync(default!);
        Transactions().Single().TransactItems[1].Update.ExpressionAttributeValues[":title"].S.Should().Be("Spirited Away");
    }

    [Fact]
    public async Task MovieSearch_ATooShortQuery_IsAnEmptyCacheableAnswerWithoutAskingTmdb()
    {
        var (response, cacheControl) = await new MovieSearchService(_adapter, _movieLookup).ExecuteAsync("a");

        response.Movies.Should().BeEmpty();
        cacheControl.Should().Be(MovieSearchService.CacheForADay);
        await _movieLookup.DidNotReceiveWithAnyArgs().SearchAsync(default!);
    }

    [Fact]
    public async Task MovieSearch_Found_IsCachedForADayWithThumbnailPosters()
    {
        _movieLookup.SearchAsync("toy").Returns(new MovieSearch
        {
            Succeeded = true,
            Movies = [new MovieMatch { TmdbId = 862, Title = "Toy Story", Year = "1995", PosterPath = "/toy.jpg" }]
        });

        var (response, cacheControl) = await new MovieSearchService(_adapter, _movieLookup).ExecuteAsync("toy");

        response.Movies.Single().Should().BeEquivalentTo(new MovieOption
        {
            TmdbId = 862, Title = "Toy Story", Year = "1995", PosterUrl = "https://image.tmdb.org/t/p/w92/toy.jpg"
        });
        cacheControl.Should().Be("public, max-age=86400");
    }

    [Fact]
    public async Task MovieSearch_TmdbFailed_IsEmptyAndNeverCached()
    {
        _movieLookup.SearchAsync(Arg.Any<string>()).Returns(MovieSearch.Unavailable);

        var (response, cacheControl) = await new MovieSearchService(_adapter, _movieLookup).ExecuteAsync("alien");

        response.Movies.Should().BeEmpty();
        cacheControl.Should().Be("no-store");
    }

    [Fact]
    public async Task SubmitSuggestion_ATitleAlreadyTyped_IsTurnedAwayWithoutAskingTmdb()
    {
        GivenTheNightReads(Night(NightPhase.Collecting, Person("a", "Alien"), Person("b")));

        await SubmitSuggestionService().ExecuteAsync(Suggestion("b", "alien"));

        await _movieLookup.DidNotReceiveWithAnyArgs().FindAsync(default!);
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

    private ConfirmNightService ConfirmNightService() => new(_adapter, _nightReader, _phaseAdvancer);

    private OptOutService OptOutService() => new(_adapter, _nightReader, _nightProvider, _doNotEmail, _phaseAdvancer);

    private void GivenListed(string email) =>
        _dynamoDb.GetItemAsync(
                Arg.Is<GetItemRequest>(request => request.Key["PK"].S == DoNotEmailProvider.KeyFor(email)),
                Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["PK"] = new() { S = DoNotEmailProvider.KeyFor(email) } }
            });

    private SubmitSuggestionService SubmitSuggestionService() => new(_adapter, _nightReader, _nightProvider, _phaseAdvancer, _movieLookup);

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
