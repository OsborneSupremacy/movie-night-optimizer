using System.Globalization;

namespace MovieNight.Library.Services;

/// <summary>
/// What one participant is shown of a night. The one place that decides what leaves the API, so
/// that nobody's own suggestion is ever in their list to rank and nobody sees the others'
/// suggestions while they could still change their own.
/// </summary>
internal static class NightViewBuilder
{
    public static NightView Build(Night night, Participant viewer) =>
        new()
        {
            NightId = night.NightId,
            Date = night.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Phase = night.Phase.ToString().ToLowerInvariant(),
            IsOrganizer = viewer.IsOrganizer,
            YourSuggestion = viewer.Suggestion,
            HasAnswered = NightProgress.HasAnswered(night, viewer),
            ParticipantCount = night.Participants.Count,
            AnsweredCount = night.Participants.Count(participant => NightProgress.HasAnswered(night, participant)),
            ToRank = night.Phase == NightPhase.Ranking
                ? NightProgress.MoviesToRank(night, viewer.ParticipantId)
                    .Select(movie => new MovieView { MovieId = movie.ParticipantId, Title = movie.Suggestion })
                    .ToImmutableList()
                : [],
            Results = night.Phase == NightPhase.Done
                ? NightProgress.Score(night)
                    .Select(score => new ResultView
                    {
                        MovieId = score.MovieId,
                        Title = NightProgress.TitleOf(night, score.MovieId),
                        Points = score.Points,
                        FirstPlaceVotes = score.FirstPlaceVotes,
                        IsYours = score.MovieId == viewer.ParticipantId
                    })
                    .ToImmutableList()
                : [],
            WaitingOn = viewer.IsOrganizer
                ? night.Participants
                    .Where(participant => !NightProgress.HasAnswered(night, participant))
                    .Select(participant => participant.Email)
                    .ToImmutableList()
                : []
        };
}
