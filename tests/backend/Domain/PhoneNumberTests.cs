using Soro.Domain.Common;

namespace Soro.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0707070707", "+2250707070707")]
    [InlineData("07 07 07 07 07", "+2250707070707")]
    [InlineData("07.07.07.07.07", "+2250707070707")]
    [InlineData("07-07-07-07-07", "+2250707070707")]
    [InlineData("  0707070707  ", "+2250707070707")]
    [InlineData("+2250707070707", "+2250707070707")]
    [InlineData("+225 07 07 07 07 07", "+2250707070707")]
    [InlineData("002250707070707", "+2250707070707")]
    [InlineData("2250707070707", "+2250707070707")]
    [InlineData("(+225) 01 02 03 04 05", "+2250102030405")]
    public void Accepts_the_usual_ivorian_formats_and_gives_one_canonical_form(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Parse(input).Value);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("07070707")] // ancien format à 8 chiffres
    [InlineData("070707070")]
    [InlineData("07070707070")]
    [InlineData("abc0707070707")]
    [InlineData("+3360707070707")] // autre pays : hors lot
    [InlineData("+22507070707")]
    [InlineData("+22507070707070")]
    [InlineData("07 07 07 07 0x")]
    public void Rejects_anything_else(string input) =>
        Assert.Throws<DomainException>(() => PhoneNumber.Parse(input));

    [Fact]
    public void Rejects_null() => Assert.Throws<DomainException>(() => PhoneNumber.Parse(null));

    [Fact]
    public void Two_spellings_of_the_same_number_are_equal()
    {
        Assert.Equal(PhoneNumber.Parse("07 07 07 07 07"), PhoneNumber.Parse("+2250707070707"));
    }

    [Fact]
    public void Tells_whether_a_text_is_a_valid_number_without_throwing()
    {
        Assert.True(PhoneNumber.TryParse("0707070707", out var number));
        Assert.Equal("+2250707070707", number.Value);
        Assert.False(PhoneNumber.TryParse("123", out _));
    }

    [Fact]
    public void Masks_the_middle_of_the_number_for_messages_and_logs() =>
        Assert.Equal("+225 07 ** ** ** 07", PhoneNumber.Parse("0707070707").Masked);
}
