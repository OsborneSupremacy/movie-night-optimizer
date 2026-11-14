namespace MovieNight.Library.Abstractions;

/// <summary>
/// Finds the movie somebody meant from what they typed.
/// </summary>
/// <remarks>
/// An enhancement, never a dependency: an implementation answers <see cref="MovieMatch.None"/>
/// rather than throwing, whatever goes wrong, so a suggestion is saved as typed when it can't help.
/// </remarks>
public interface IMovieLookup
{
    /// <summary>The one movie somebody most likely meant, with its details.</summary>
    Task<MovieMatch> FindAsync(string typedTitle);

    /// <summary>
    /// The movie with this TMDB id, with its details: what somebody picked from the autocomplete
    /// list. <see cref="MovieMatch.None"/> when TMDB can't be asked or doesn't know the id.
    /// </summary>
    Task<MovieMatch> FindByIdAsync(int tmdbId);

    /// <summary>
    /// A few movies that could be what somebody is typing, best first, without their details.
    /// <see cref="MovieSearch.Unavailable"/> when TMDB couldn't be asked, as opposed to a search
    /// that found nothing, so that a failure isn't cached as if it were an answer.
    /// </summary>
    Task<MovieSearch> SearchAsync(string partialTitle);
}
