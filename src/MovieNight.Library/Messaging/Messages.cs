namespace MovieNight.Library.Messaging;

public record ErrorResponse
{
    public required string Message { get; init; }
}

/// <summary>For a handler whose answer is its status code and nothing else.</summary>
public record StatusCodeOnlyResponse;

public record HealthResponse
{
    public required string Status { get; init; }
}

public record CreateNightRequest
{
    public string OrganizerEmail { get; init; } = string.Empty;

    /// <summary>Everyone watching, other than the organizer.</summary>
    public ImmutableList<string> ParticipantEmails { get; init; } = [];

    /// <summary>The date of the movie night, as yyyy-MM-dd.</summary>
    public string Date { get; init; } = string.Empty;
}

public record CreateNightResponse
{
    public required string NightId { get; init; }

    /// <summary>Everyone emailed a link, the organizer included.</summary>
    public required int InvitedCount { get; init; }

    /// <summary>Guests left out because they have asked never to be emailed.</summary>
    public required int OptedOutCount { get; init; }
}

/// <summary>Who is asking about which night: the path and the token header, for a GET or a bodiless POST.</summary>
public record NightRequest
{
    public required string NightId { get; init; }

    public required string Token { get; init; }
}

/// <summary>The body of POST /api/nights/{nightId}/suggestion.</summary>
public record SuggestionBody
{
    public string Suggestion { get; init; } = string.Empty;
}

public record SubmitSuggestionRequest
{
    public required string NightId { get; init; }

    public required string Token { get; init; }

    public required string Suggestion { get; init; }
}

/// <summary>The body of POST /api/nights/{nightId}/ranking.</summary>
public record RankingBody
{
    /// <summary>Movie ids from <see cref="NightView.ToRank"/>, best first.</summary>
    public ImmutableList<string> Ranking { get; init; } = [];
}

public record SubmitRankingRequest
{
    public required string NightId { get; init; }

    public required string Token { get; init; }

    public required ImmutableList<string> Ranking { get; init; }
}

/// <summary>
/// What one participant sees of a night. Every endpoint that takes a participant token answers
/// with this, so the page can redraw from whatever it last did.
/// </summary>
public record NightView
{
    public required string NightId { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public required string Date { get; init; }

    /// <summary>"collecting", "ranking" or "done".</summary>
    public required string Phase { get; init; }

    public required bool IsOrganizer { get; init; }

    /// <summary>This participant's own movie, or empty until they have suggested one.</summary>
    public required string YourSuggestion { get; init; }

    /// <summary>The year of this participant's movie, or empty.</summary>
    public required string YourYear { get; init; }

    /// <summary>The poster of this participant's movie, or empty.</summary>
    public required string YourPosterUrl { get; init; }

    /// <summary>The rating, runtime and so on of this participant's movie, as far as TMDB knew them.</summary>
    public required MovieDetails YourDetails { get; init; }

    /// <summary>
    /// Whether this participant has answered the current round. Somebody with nothing to rank
    /// counts as having ranked.
    /// </summary>
    public required bool HasAnswered { get; init; }

    public required int ParticipantCount { get; init; }

    /// <summary>How many people have answered the current round.</summary>
    public required int AnsweredCount { get; init; }

    /// <summary>
    /// While ranking, the movies this participant ranks: everyone's but their own. Empty in any
    /// other phase, so nobody sees the others' suggestions before they have made their own.
    /// </summary>
    public required ImmutableList<MovieView> ToRank { get; init; }

    /// <summary>When done, every movie, winner first. Empty before.</summary>
    public required ImmutableList<ResultView> Results { get; init; }

    /// <summary>
    /// For the organizer only, the addresses of who hasn't answered the current round, so they
    /// can decide whether to close it. Empty for everyone else.
    /// </summary>
    public required ImmutableList<string> WaitingOn { get; init; }
}

public record MovieView
{
    public required string MovieId { get; init; }

    public required string Title { get; init; }

    /// <summary>The year it came out, or empty when unknown.</summary>
    public required string Year { get; init; }

    /// <summary>A small poster from TMDB, or empty.</summary>
    public required string PosterUrl { get; init; }

    /// <summary>The rating, runtime, genres, streaming services and trailer, as far as TMDB knew them.</summary>
    public required MovieDetails Details { get; init; }
}

public record ResultView
{
    public required string MovieId { get; init; }

    public required string Title { get; init; }

    /// <summary>The year it came out, or empty when unknown.</summary>
    public required string Year { get; init; }

    /// <summary>A small poster from TMDB, or empty.</summary>
    public required string PosterUrl { get; init; }

    /// <summary>The rating, runtime, genres, streaming services and trailer, as far as TMDB knew them.</summary>
    public required MovieDetails Details { get; init; }

    public required int Points { get; init; }

    public required int FirstPlaceVotes { get; init; }

    public required bool IsYours { get; init; }
}
