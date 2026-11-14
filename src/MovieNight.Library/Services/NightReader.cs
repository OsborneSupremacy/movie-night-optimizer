namespace MovieNight.Library.Services;

/// <summary>
/// Finds the night and the participant behind a token. Every endpoint but creating a night starts
/// here.
/// </summary>
/// <remarks>
/// A night that doesn't exist and a token that isn't in it get the same 404 and the same words,
/// so a guessed night id says nothing about whether the night is real.
/// </remarks>
[UsedImplicitly]
internal class NightReader
{
    internal const string NotFoundMessage = "This link doesn't match a movie night. Open the link from your most recent email.";

    private readonly NightProvider _nightProvider;

    // ReSharper disable once ConvertToPrimaryConstructor
    public NightReader(NightProvider nightProvider)
    {
        _nightProvider = nightProvider ?? throw new ArgumentNullException(nameof(nightProvider));
    }

    public async Task<Result<Caller>> FindCallerAsync(string nightId, string token)
    {
        if(string.IsNullOrWhiteSpace(nightId) || string.IsNullOrWhiteSpace(token))
            return NotFound<Caller>();

        var (found, night) = await _nightProvider.TryGetNightAsync(nightId).ConfigureAwait(false);

        if(!found)
            return NotFound<Caller>();

        var (isParticipant, participant) = NightProgress.FindByToken(night, token);

        return isParticipant
            ? new Result<Caller>(new Caller { Night = night, Participant = participant }, HttpStatusCode.OK)
            : NotFound<Caller>();
    }

    /// <summary>Reads the night afresh and builds what this participant sees of it.</summary>
    public async Task<Result<NightView>> ViewAsync(string nightId, string token)
    {
        var caller = await FindCallerAsync(nightId, token).ConfigureAwait(false);

        return caller.IsFaulted
            ? new Result<NightView>(caller.Exception, caller.StatusCode)
            : new Result<NightView>(NightViewBuilder.Build(caller.Value.Night, caller.Value.Participant), HttpStatusCode.OK);
    }

    private static Result<T> NotFound<T>() =>
        new(new InvalidOperationException(NotFoundMessage), HttpStatusCode.NotFound);
}
