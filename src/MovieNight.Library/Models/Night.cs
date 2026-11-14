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

    /// <summary>The hash of the token in this participant's links. The token itself is never stored.</summary>
    public required string TokenHash { get; init; }

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

    public required string NightId { get; init; }

    public required DateOnly Date { get; init; }

    public required NightPhase Phase { get; init; }

    public required ImmutableList<Participant> Participants { get; init; }
}
