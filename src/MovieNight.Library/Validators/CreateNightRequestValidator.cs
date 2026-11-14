using System.Globalization;

namespace MovieNight.Library.Validators;

[UsedImplicitly]
internal class CreateNightRequestValidator : AbstractValidator<CreateNightRequest>
{
    /// <summary>
    /// The most people in one night, organizer included. A cap on how much mail one request can
    /// send as much as a limit on the size of a sofa.
    /// </summary>
    internal const int MaxPeople = 8;

    public CreateNightRequestValidator()
    {
        RuleFor(request => request.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        // A request body can say null where the record says it can't, so everything below reads
        // through Emails() rather than trusting the property.
        RuleFor(request => request)
            .Must(request => Emails(request).Count >= 1)
            .WithMessage("Add at least one other person.")
            .Must(request => Emails(request).Count <= MaxPeople - 1)
            .WithMessage($"A movie night can have at most {MaxPeople} people.")
            .Must(request => Emails(request)
                .Append(request.OrganizerEmail ?? string.Empty)
                .Select(email => email.Trim().ToLowerInvariant())
                .Distinct()
                .Count() == Emails(request).Count + 1)
            .WithMessage("Each person can only be added once.");

        RuleForEach(request => request.ParticipantEmails)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(request => request.Date)
            .Must(date => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("The date must be in the form yyyy-MM-dd.");
    }

    private static ImmutableList<string> Emails(CreateNightRequest request) =>
        (request.ParticipantEmails ?? [])
            .Select(email => email ?? string.Empty)
            .ToImmutableList();
}
