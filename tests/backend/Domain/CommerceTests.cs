using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Domain.Tests;

public class CommerceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("0707070707");

    private static Commerce Open(string name = "  Supérette du Carrefour ", string terms = "2026-10-brouillon", int trialDays = 30) =>
        Commerce.Open(TenantId.New(), name, ActivityType.Supermarket, Phone, terms, Now, trialDays);

    [Fact]
    public void Opens_with_a_trimmed_name_the_owner_phone_and_a_thirty_day_trial()
    {
        var commerce = Open();

        Assert.Equal("Supérette du Carrefour", commerce.Name);
        Assert.Equal(Phone, commerce.OwnerPhone);
        Assert.Equal(ActivityType.Supermarket, commerce.Activity);
        Assert.Equal(Now, commerce.CreatedAt);
        Assert.Equal(Now.AddDays(30), commerce.TrialEndsAt);
        Assert.Equal("Commerce", commerce.Formula);
    }

    [Fact]
    public void Records_which_terms_were_accepted_and_when()
    {
        var commerce = Open(terms: "2026-10-brouillon");

        Assert.Equal("2026-10-brouillon", commerce.TermsVersion);
        Assert.Equal(Now, commerce.TermsAcceptedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    public void Refuses_a_missing_or_one_letter_name(string name) =>
        Assert.Throws<DomainException>(() => Open(name));

    [Fact]
    public void Refuses_a_name_over_a_hundred_characters() =>
        Assert.Throws<DomainException>(() => Open(new string('a', 101)));

    [Fact]
    public void Refuses_to_open_without_accepted_terms() =>
        Assert.Throws<DomainException>(() => Open(terms: " "));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(400)]
    public void Refuses_a_nonsensical_trial_length(int days) =>
        Assert.Throws<DomainException>(() => Open(trialDays: days));

    [Fact]
    public void Knows_whether_the_trial_is_over()
    {
        var commerce = Open();

        Assert.False(commerce.IsTrialOver(Now.AddDays(29)));
        Assert.True(commerce.IsTrialOver(Now.AddDays(30)));
    }
}
