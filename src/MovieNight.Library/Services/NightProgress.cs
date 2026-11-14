namespace MovieNight.Library.Services;

/// <summary>
/// The rules of a night that depend only on what is in it: who has answered, who ranks what,
/// where it goes next and how it scores. Nothing here reads or writes anything.
/// </summary>
/// <remarks>
/// A movie is identified by the participant who suggested it, so a movie id is a participant id.
/// </remarks>
internal static class NightProgress
{
    /// <summary>The longest movie title accepted, which is longer than any real one.</summary>
    internal const int MaxSuggestionLength = 100;

    /// <summary>Everyone who has suggested a movie.</summary>
    public static ImmutableList<Participant> Suggesters(Night night) =>
        night.Participants
            .Where(participant => participant.Suggestion.Length > 0)
            .ToImmutableList();

    /// <summary>
    /// The movies this participant ranks: every suggestion but their own.
    /// </summary>
    /// <remarks>
    /// Shuffled, and differently for each participant, because a list people are asked to reorder
    /// is a list some of them will leave alone, and whichever movie happened to be first would
    /// collect the points. Shuffled the same way every time, so the page doesn't reorder itself
    /// on a refresh.
    /// </remarks>
    public static ImmutableList<Participant> MoviesToRank(Night night, string participantId) =>
        Suggesters(night)
            .Where(suggester => suggester.ParticipantId != participantId)
            .OrderBy(suggester => SecretToken.Hash($"{participantId}:{suggester.ParticipantId}"), StringComparer.Ordinal)
            .ToImmutableList();

    /// <summary>Whether this participant still owes the night a ranking.</summary>
    public static bool NeedsToRank(Night night, Participant participant) =>
        !participant.OptedOut
        && participant.Ranking.Count == 0
        && MoviesToRank(night, participant.ParticipantId).Count > 0;

    /// <summary>
    /// Whether this participant has answered the round the night is in. Somebody who has opted out
    /// of email counts as answered, because nobody can reach them to ask.
    /// </summary>
    public static bool HasAnswered(Night night, Participant participant) =>
        participant.OptedOut || night.Phase switch
        {
            NightPhase.Collecting => participant.Suggestion.Length > 0,
            NightPhase.Ranking => !NeedsToRank(night, participant),
            _ => true
        };

    /// <summary>Whether everyone has answered the round the night is in.</summary>
    public static bool IsRoundComplete(Night night) =>
        night.Participants.All(participant => HasAnswered(night, participant));

    /// <summary>
    /// Where the night goes when the current round closes.
    /// </summary>
    /// <remarks>
    /// With one suggestion or none there is nothing to choose between, so collecting goes straight
    /// to done rather than asking people to rank a list of one.
    /// </remarks>
    public static NightPhase NextPhase(Night night) =>
        night.Phase == NightPhase.Collecting && Suggesters(night).Count > 1
            ? NightPhase.Ranking
            : NightPhase.Done;

    /// <summary>
    /// Whether a ranking names every movie this participant is to rank, each once, and nothing else.
    /// </summary>
    /// <remarks>
    /// All of them rather than a top few, because a Borda count is only fair when every ballot
    /// ranks the same number of movies.
    /// </remarks>
    public static bool IsCompleteRanking(Night night, string participantId, IReadOnlyCollection<string> ranking)
    {
        var expected = MoviesToRank(night, participantId)
            .Select(movie => movie.ParticipantId)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return ranking.Count == expected.Count
            && ranking.Distinct(StringComparer.Ordinal).Count() == ranking.Count
            && ranking.All(expected.Contains);
    }

    /// <summary>Every suggestion, winner first, from the rankings that are in.</summary>
    public static ImmutableList<MovieScore> Score(Night night) =>
        BordaScorer.Score(
            Suggesters(night).Select(suggester => suggester.ParticipantId).ToImmutableList(),
            night.Participants
                .Where(participant => participant.Ranking.Count > 0)
                .Select(participant => new Ballot
                {
                    VoterId = participant.ParticipantId,
                    RankedMovieIds = participant.Ranking
                })
                .ToImmutableList()
        );

    /// <summary>
    /// Whether somebody else in the night has already suggested this movie.
    /// </summary>
    /// <remarks>
    /// Turned away rather than merged. Two entries for one movie split its votes, and merging
    /// them would leave the merged movie off two ballots instead of one, which a Borda count
    /// can't compare fairly. Asking the second person for another pick keeps one movie per person.
    ///
    /// The participant's own earlier suggestion doesn't count, so they can resubmit it. Two people
    /// sending the same new title at the same instant can both get through; that is rare enough,
    /// and harmless enough, to accept.
    ///
    /// When TMDB found both movies, its ids decide, so "Paddington Two" is Paddington 2 while Dune
    /// and "Dune 1984" stay two films. When it didn't find one of them, the titles decide.
    /// </remarks>
    /// <param name="tmdbId">TMDB's id for the suggestion, or 0 when it wasn't found.</param>
    public static bool IsTakenByAnother(Night night, string participantId, string suggestion, int tmdbId = 0)
    {
        var key = TitleKey(suggestion);

        return key.Length > 0 && night.Participants
            .Where(participant => participant.ParticipantId != participantId && participant.Suggestion.Length > 0)
            .Any(participant => tmdbId > 0 && participant.TmdbId > 0
                ? participant.TmdbId == tmdbId
                : TitleKey(participant.Suggestion) == key);
    }

    /// <summary>
    /// A title reduced to what tells movies apart: letters and digits, lower-cased, without a
    /// leading article. "The Princess Bride!" and "princess  bride" are the same movie.
    /// </summary>
    internal static string TitleKey(string title)
    {
        var words = new string(title
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
                .ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableList();

        if(words.Count > 1 && words[0] is "the" or "a" or "an")
            words = words.RemoveAt(0);

        return string.Join(' ', words);
    }

    /// <summary>A participant's movie as people read it: "Alien (1979)", or just the title when the year is unknown.</summary>
    public static string DisplayTitle(Participant suggester) =>
        suggester.MovieYear.Length > 0
            ? $"{suggester.Suggestion} ({suggester.MovieYear})"
            : suggester.Suggestion;

    /// <summary>
    /// The full address of a TMDB poster, or empty for none. <paramref name="width"/> is one of TMDB's
    /// sizes: "w92", "w154", "w185", "w342", "w500".
    /// </summary>
    public static string PosterUrl(string posterPath, string width) =>
        posterPath.Length > 0
            ? $"https://image.tmdb.org/t/p/{width}{posterPath}"
            : string.Empty;

    /// <summary>The participant who suggested a movie, by the movie's id.</summary>
    public static Participant SuggesterOf(Night night, string movieId) =>
        night.Participants.FirstOrDefault(participant => participant.ParticipantId == movieId) ?? Night.EmptyParticipant;

    /// <summary>The title of the movie a participant suggested, by its id.</summary>
    public static string TitleOf(Night night, string movieId) =>
        night.Participants
            .Where(participant => participant.ParticipantId == movieId)
            .Select(participant => participant.Suggestion)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>The participant whose token this is, if any.</summary>
    public static (bool found, Participant participant) FindByToken(Night night, string token)
    {
        if(string.IsNullOrWhiteSpace(token))
            return (false, Night.EmptyParticipant);

        var hash = SecretToken.Hash(token.Trim());

        var participant = night.Participants
            .FirstOrDefault(candidate => candidate.TokenHashes.Contains(hash, StringComparer.Ordinal));

        return participant is null
            ? (false, Night.EmptyParticipant)
            : (true, participant);
    }
}
