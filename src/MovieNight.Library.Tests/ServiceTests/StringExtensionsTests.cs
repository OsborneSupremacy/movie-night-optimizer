namespace MovieNight.Library.Tests.ServiceTests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("  Ben@Example.COM ", "ben@example.com")]
    [InlineData("ben+movies@example.com", "ben@example.com")]
    [InlineData("b.e.n+1@gmail.com", "ben@gmail.com")]
    [InlineData("ben@googlemail.com", "ben@gmail.com")]
    [InlineData("b.en@example.com", "b.en@example.com")]
    [InlineData("+tag@example.com", "+tag@example.com")]
    [InlineData("not-an-address", "not-an-address")]
    public void ToMailboxKey_GivesOneKeyPerInbox(string address, string expected) =>
        address.ToMailboxKey().Should().Be(expected);
}
