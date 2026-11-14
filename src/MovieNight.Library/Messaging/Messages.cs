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
}
