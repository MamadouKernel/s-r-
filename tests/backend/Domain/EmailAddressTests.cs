using Soro.Domain.Common;

namespace Soro.Domain.Tests;

public class EmailAddressTests
{
    [Theory]
    [InlineData("mamadou@exemple.ci", "mamadou@exemple.ci")]
    [InlineData("  Mamadou@Exemple.CI  ", "mamadou@exemple.ci")]
    [InlineData("MAMADOU.KONATE@Gmail.com", "mamadou.konate@gmail.com")]
    [InlineData("a_b-c+d@sous.domaine.exemple.ci", "a_b-c+d@sous.domaine.exemple.ci")]
    [InlineData("boutique2026@orange.ci", "boutique2026@orange.ci")]
    public void Accepts_usual_addresses_and_gives_one_canonical_form(string input, string expected) =>
        Assert.Equal(expected, EmailAddress.Parse(input).Value);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mamadou")]
    [InlineData("mamadou@")]
    [InlineData("@exemple.ci")]
    [InlineData("mamadou@exemple")]
    [InlineData("mamadou@@exemple.ci")]
    [InlineData("ma@madou@exemple.ci")]
    [InlineData("mama dou@exemple.ci")]
    [InlineData(".mamadou@exemple.ci")]
    [InlineData("mamadou.@exemple.ci")]
    [InlineData("ma..madou@exemple.ci")]
    [InlineData("mamadou@-exemple.ci")]
    [InlineData("mamadou@exemple-.ci")]
    [InlineData("mamadou@exemple..ci")]
    [InlineData("mamadou@exemple.c")]
    [InlineData("mamadou@exemple.c1")]
    [InlineData("mamadou,konate@exemple.ci")]
    [InlineData("0707070707")]
    public void Rejects_anything_that_is_not_an_address(string input) =>
        Assert.Throws<DomainException>(() => EmailAddress.Parse(input));

    [Fact]
    public void Rejects_null() => Assert.Throws<DomainException>(() => EmailAddress.Parse(null));

    [Fact]
    public void Rejects_an_address_that_is_too_long()
    {
        Assert.Throws<DomainException>(() => EmailAddress.Parse(new string('a', 65) + "@exemple.ci"));
        Assert.Throws<DomainException>(() => EmailAddress.Parse("a@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 5)) + ".ci"));
    }

    [Fact]
    public void Two_spellings_of_the_same_address_are_equal() =>
        Assert.Equal(EmailAddress.Parse(" Mamadou@Exemple.ci"), EmailAddress.Parse("mamadou@EXEMPLE.CI"));

    [Fact]
    public void Tells_whether_a_text_is_a_valid_address_without_throwing()
    {
        Assert.True(EmailAddress.TryParse("Mamadou@exemple.ci", out var address));
        Assert.Equal("mamadou@exemple.ci", address.Value);
        Assert.Equal("mamadou", address.LocalPart);
        Assert.False(EmailAddress.TryParse("123", out _));
    }

    [Fact]
    public void Hides_most_of_the_address_for_logs() =>
        Assert.Equal("m***@exemple.ci", EmailAddress.Parse("mamadou@exemple.ci").Masked);
}
