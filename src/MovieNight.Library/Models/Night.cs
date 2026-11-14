namespace MovieNight.Library.Models;

/// <summary>
/// Where a movie night is. It only ever moves forward.
/// </summary>
public enum NightPhase
{
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

    /// <summary>The movie they suggested, or empty until they have.</summary>
    public string Suggestion { get; init; } = string.Empty;

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
