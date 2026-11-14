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
    Task<MovieMatch> FindAsync(string typedTitle);
}
