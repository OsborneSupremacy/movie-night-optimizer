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
        PosterPath = string.Empty
    };

    /// <summary>TMDB's id for the movie, or 0 for no match.</summary>
    public required int TmdbId { get; init; }

    /// <summary>TMDB's title for the movie, which replaces whatever was typed.</summary>
    public required string Title { get; init; }

    /// <summary>The year of first release, or empty when TMDB doesn't know it.</summary>
    public required string Year { get; init; }

    /// <summary>TMDB's poster path, like "/abc.jpg", or empty when it has none.</summary>
    public required string PosterPath { get; init; }

    public bool IsFound => TmdbId > 0;
}
