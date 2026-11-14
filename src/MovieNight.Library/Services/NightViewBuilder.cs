using System.Globalization;

namespace MovieNight.Library.Services;

/// <summary>
/// What one participant is shown of a night. The one place that decides what leaves the API, so
/// that nobody's own suggestion is ever in their list to rank and nobody sees the others'
/// suggestions while they could still change their own.
/// </summary>
internal static class NightViewBuilder
{
    /// <summary>Big enough for a crisp thumbnail on a phone's screen, small enough to load fast.</summary>
    private const string PosterWidth = "w185";

    public static NightView Build(Night night, Participant viewer) =>
        new()
        {
            NightId = night.NightId,
            Date = night.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Phase = night.Phase.ToString().ToLowerInvariant(),
            IsOrganizer = viewer.IsOrganizer,
            YourSuggestion = viewer.Suggestion,
            YourYear = viewer.MovieYear,
            YourPosterUrl = NightProgress.PosterUrl(viewer.PosterPath, PosterWidth),
            YourDetails = viewer.Details,
            HasAnswered = NightProgress.HasAnswered(night, viewer),
            ParticipantCount = night.Participants.Count,
            AnsweredCount = night.Participants.Count(participant => NightProgress.HasAnswered(night, participant)),
            ToRank = night.Phase == NightPhase.Ranking
                ? NightProgress.MoviesToRank(night, viewer.ParticipantId)
                    .Select(movie => new MovieView
                    {
                        MovieId = movie.ParticipantId,
                        Title = movie.Suggestion,
                        Year = movie.MovieYear,
                        PosterUrl = NightProgress.PosterUrl(movie.PosterPath, PosterWidth),
                        Details = movie.Details
                    })
                    .ToImmutableList()
                : [],
            Results = night.Phase == NightPhase.Done
                ? NightProgress.Score(night)
                    .Select(score => (score, movie: NightProgress.SuggesterOf(night, score.MovieId)))
                    .Select(scored => new ResultView
                    {
                        MovieId = scored.score.MovieId,
                        Title = scored.movie.Suggestion,
                        Year = scored.movie.MovieYear,
                        PosterUrl = NightProgress.PosterUrl(scored.movie.PosterPath, PosterWidth),
                        Details = scored.movie.Details,
                        Points = scored.score.Points,
                        FirstPlaceVotes = scored.score.FirstPlaceVotes,
                        IsYours = scored.score.MovieId == viewer.ParticipantId
                    })
                    .ToImmutableList()
                : [],
            Guests = viewer.IsOrganizer && night.Phase == NightPhase.Pending
                ? night.Participants
                    .Where(participant => !participant.IsOrganizer && !participant.OptedOut)
                    .Select(participant => participant.Email)
                    .ToImmutableList()
                : [],
            WaitingOn = viewer.IsOrganizer && night.Phase != NightPhase.Pending
                ? night.Participants
                    .Where(participant => !NightProgress.HasAnswered(night, participant))
                    .Select(participant => participant.Email)
                    .ToImmutableList()
                : []
        };
}
