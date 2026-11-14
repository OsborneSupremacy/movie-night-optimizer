namespace MovieNight.Library.Models;

/// <summary>
/// The movie TMDB found for what somebody typed, or <see cref="None"/> when it found nothing or
/// couldn't be asked.
/// </summary>
public record MovieMatch
{
    public static readonly MovieMatch None = new()
    {
        TmdbId = 0,
        Title = string.Empty,
        Year = string.Empty,
        PosterPath = string.Empty,
        Details = MovieDetails.Empty
    };

    /// <summary>TMDB's id for the movie, or 0 for no match.</summary>
    public required int TmdbId { get; init; }

    /// <summary>TMDB's title for the movie, which replaces whatever was typed.</summary>
    public required string Title { get; init; }

    /// <summary>The year of first release, or empty when TMDB doesn't know it.</summary>
    public required string Year { get; init; }

    /// <summary>TMDB's poster path, like "/abc.jpg", or empty when it has none.</summary>
    public required string PosterPath { get; init; }

    /// <summary>What helps people rank a movie they don't know. <see cref="MovieDetails.Empty"/> when TMDB couldn't say.</summary>
    public MovieDetails Details { get; init; } = MovieDetails.Empty;

    public bool IsFound => TmdbId > 0;
}

/// <summary>
/// The facts about a movie that help somebody rank it without having seen it. Any of them can be
/// missing, and each is shown only when it isn't.
/// </summary>
public record MovieDetails
{
    public static readonly MovieDetails Empty = new()
    {
        Certification = string.Empty,
        RuntimeMinutes = 0,
        Genres = [],
        StreamingOn = [],
        TrailerUrl = string.Empty
    };

    /// <summary>The US rating, like "PG-13", or empty.</summary>
    public required string Certification { get; init; }

    /// <summary>The running time, or 0 when unknown.</summary>
    public required int RuntimeMinutes { get; init; }

    /// <summary>At most three genres, TMDB's order.</summary>
    public required ImmutableList<string> Genres { get; init; }

    /// <summary>
    /// At most three US services it streams on with a subscription or for free, from JustWatch by
    /// way of TMDB. Rent and buy are left out: the question is whether it can be watched tonight.
    /// </summary>
    public required ImmutableList<string> StreamingOn { get; init; }

    /// <summary>A YouTube trailer, or empty.</summary>
    public required string TrailerUrl { get; init; }
}

/// <summary>The movies an autocomplete search found, and whether TMDB could be asked at all.</summary>
public record MovieSearch
{
    public static readonly MovieSearch Unavailable = new() { Succeeded = false, Movies = [] };

    public required bool Succeeded { get; init; }

    public required ImmutableList<MovieMatch> Movies { get; init; }
}
