namespace MovieNight.Library.Tests.ValidatorTests;

public class CreateNightRequestValidatorTests
{
    private static readonly CreateNightRequest ValidRequest = new()
    {
        OrganizerEmail = "organizer@example.com",
        ParticipantEmails = ["one@example.com", "two@example.com"],
        Date = "2026-11-13"
    };

    private readonly CreateNightRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes() =>
        _validator.Validate(ValidRequest).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_NoOtherParticipants_Fails() =>
        _validator.Validate(ValidRequest with { ParticipantEmails = [] }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_MoreThanTheMaximum_Fails()
    {
        var tooMany = Enumerable
            .Range(0, CreateNightRequestValidator.MaxPeople)
            .Select(index => $"person{index}@example.com")
            .ToImmutableList();

        _validator.Validate(ValidRequest with { ParticipantEmails = tooMany }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SameAddressTwiceInADifferentCase_Fails() =>
        _validator
            .Validate(ValidRequest with { ParticipantEmails = ["one@example.com", "ONE@example.com"] })
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_OrganizerAlsoListedAsParticipant_Fails() =>
        _validator
            .Validate(ValidRequest with { ParticipantEmails = ["organizer@example.com"] })
            .IsValid.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("11/13/2026")]
    [InlineData("2026-13-45")]
    public void Validate_DateNotInTheExpectedForm_Fails(string date) =>
        _validator.Validate(ValidRequest with { Date = date }).IsValid.Should().BeFalse();
}
