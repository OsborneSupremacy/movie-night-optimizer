namespace MovieNight.Library.Models;

/// <summary>
/// Where a movie night is. It only ever moves forward.
/// </summary>
public enum NightPhase
{
    /// <summary>
    /// Waiting for the organizer to confirm, from the email sent to the address they typed, that
    /// the address is theirs. Nobody else has been emailed.
    /// </summary>
    /// <remarks>
    /// Stored by name, so adding this ahead of the others changes nothing already saved.
    /// </remarks>
    Pending,

    /// <summary>Participants are suggesting one movie each.</summary>
    Collecting,

    /// <summary>Every suggestion is in, and participants are ranking the ones that aren't theirs.</summary>
    Ranking,

    /// <summary>Every ranking is in, and the results have been sent.</summary>
    Done
}

public record Participant
{
    public required string ParticipantId { get; init; }

    public required string Email { get; init; }

    /// <summary>
    /// The hashes of the tokens in this participant's links, one per email they have been sent.
    /// The tokens themselves are never stored.
    /// </summary>
    /// <remarks>
    /// Several because a token can't be sent twice: only its hash is kept, so each round's email
    /// carries a new one. Earlier links keep working, so whichever email somebody opens finds them.
    /// </remarks>
    public required ImmutableList<string> TokenHashes { get; init; }

    public bool IsOrganizer { get; init; }

    /// <summary>
    /// Whether they have asked never to be emailed again. They stay in the night, and a movie they
    /// suggested stays in the running, but no round waits on them and no mail goes to them.
    /// </summary>
    public bool OptedOut { get; init; }

    /// <summary>
    /// The movie they suggested, or empty until they have. TMDB's title when it found the movie,
    /// otherwise what they typed.
    /// </summary>
    public string Suggestion { get; init; } = string.Empty;

    /// <summary>TMDB's id for their movie, or 0 when it wasn't found or couldn't be asked.</summary>
    public int TmdbId { get; init; }

    /// <summary>The year their movie came out, or empty when unknown.</summary>
    public string MovieYear { get; init; } = string.Empty;

    /// <summary>TMDB's poster path for their movie, or empty.</summary>
    public string PosterPath { get; init; } = string.Empty;

    /// <summary>Their movie's rating, runtime, genres, streaming services and trailer, as far as TMDB knew them.</summary>
    public MovieDetails Details { get; init; } = MovieDetails.Empty;

    /// <summary>
    /// The participant ids whose suggestions they ranked, best first. Empty until they have.
    /// A movie is identified by the participant who suggested it.
    /// </summary>
    public ImmutableList<string> Ranking { get; init; } = [];
}

public record Night
{
    public static readonly Night Empty = new()
    {
        NightId = string.Empty,
        Date = DateOnly.MinValue,
        Phase = NightPhase.Collecting,
        Participants = []
    };

    public static readonly Participant EmptyParticipant = new()
    {
        ParticipantId = string.Empty,
        Email = string.Empty,
        TokenHashes = []
    };

    public required string NightId { get; init; }

    public required DateOnly Date { get; init; }

    public required NightPhase Phase { get; init; }

    public required ImmutableList<Participant> Participants { get; init; }
}

/// <summary>A night, and the participant whose token was presented for it.</summary>
public record Caller
{
    public required Night Night { get; init; }

    public required Participant Participant { get; init; }
}
