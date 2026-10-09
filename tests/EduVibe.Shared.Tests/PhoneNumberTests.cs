using EduVibe.Shared.Phone;

namespace EduVibe.Shared.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0771234567", "+94771234567")]
    [InlineData("077 123 4567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]
    [InlineData("+94 77-123-4567", "+94771234567")]
    [InlineData("771234567", "+94771234567")]
    [InlineData("0094771234567", "+94771234567")]
    [InlineData("+447911123456", "+447911123456")]
    public void Normalizes_to_international_form(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("077123456")]
    public void Rejects_unusable_input(string? input) => Assert.Null(PhoneNumber.Normalize(input));
}
