namespace MovieNight.Library.Validators;

[UsedImplicitly]
internal class SubmitSuggestionRequestValidator : AbstractValidator<SubmitSuggestionRequest>
{
    public SubmitSuggestionRequestValidator()
    {
        RuleFor(request => request.Suggestion)
            .NotEmpty()
            .WithMessage("Type the name of a movie.")
            .MaximumLength(NightProgress.MaxSuggestionLength)
            .WithMessage($"Keep it to {NightProgress.MaxSuggestionLength} characters.");
    }
}
