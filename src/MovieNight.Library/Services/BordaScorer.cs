namespace MovieNight.Library.Services;

/// <summary>One participant's ranking, best first.</summary>
internal record Ballot
{
    public required string VoterId { get; init; }

    public required ImmutableList<string> RankedMovieIds { get; init; }
}

internal record MovieScore
{
    public required string MovieId { get; init; }

    public required int Points { get; init; }

    public required int FirstPlaceVotes { get; init; }
}

/// <summary>
/// Scores a movie night by Borda count.
/// </summary>
/// <remarks>
/// On a ballot ranking k movies, the first gets k points and the last gets 1. Nobody ranks their
/// own suggestion, so with one suggestion each every movie appears on the same number of ballots
/// and the totals compare fairly.
///
/// That stops being true once duplicate suggestions are merged: a merged movie is left off two
/// ballots rather than one. Decide what to do about that when merging is built.
/// </remarks>
internal static class BordaScorer
{
    /// <summary>
    /// Every movie, winner first. Ties go to the movie with more first-place votes, and after that
    /// to the lower movie id, so the same ballots always give the same order. A caller that wants a
    /// coin flip for a full tie can see one coming: the leading scores are equal.
    /// </summary>
    /// <param name="movieIds">Every movie in the running, including any nobody ranked.</param>
    /// <param name="ballots">
    /// The rankings. An id on a ballot that isn't in <paramref name="movieIds"/> is ignored, and so
    /// is a second mention of the same movie on one ballot.
    /// </param>
    public static ImmutableList<MovieScore> Score(
        IReadOnlyCollection<string> movieIds,
        IReadOnlyCollection<Ballot> ballots
    )
    {
        var points = movieIds.Distinct().ToDictionary(id => id, _ => 0);
        var firstPlaceVotes = movieIds.Distinct().ToDictionary(id => id, _ => 0);

        foreach (var ballot in ballots)
        {
            var ranked = ballot.RankedMovieIds
                .Where(points.ContainsKey)
                .Distinct()
                .ToImmutableList();

            for (var position = 0; position < ranked.Count; position++)
            {
                points[ranked[position]] += ranked.Count - position;

                if(position == 0)
                    firstPlaceVotes[ranked[position]]++;
            }
        }

        return points
            .Select(entry => new MovieScore
            {
                MovieId = entry.Key,
                Points = entry.Value,
                FirstPlaceVotes = firstPlaceVotes[entry.Key]
            })
            .OrderByDescending(score => score.Points)
            .ThenByDescending(score => score.FirstPlaceVotes)
            .ThenBy(score => score.MovieId, StringComparer.Ordinal)
            .ToImmutableList();
    }
}
