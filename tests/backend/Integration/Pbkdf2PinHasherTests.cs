using System.Security.Cryptography;
using Soro.Infrastructure.Security;

namespace Soro.Integration.Tests;

public class Pbkdf2PinHasherTests
{
    private static readonly byte[] Pepper = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void A_pin_verifies_against_its_own_hash_and_not_against_another_pin()
    {
        var hasher = new Pbkdf2PinHasher(Pepper);
        var hash = hasher.Hash("1234");

        Assert.True(hasher.Verify("1234", hash));
        Assert.False(hasher.Verify("1235", hash));
    }

    [Fact]
    public void The_hash_is_salted_and_never_contains_the_pin()
    {
        var hasher = new Pbkdf2PinHasher(Pepper);

        var first = hasher.Hash("1234");
        var second = hasher.Hash("1234");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("1234", first, StringComparison.Ordinal);
        Assert.StartsWith("pbkdf2-sha256$600000$", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hash_made_with_another_pepper_does_not_verify()
    {
        var hash = new Pbkdf2PinHasher(Pepper).Hash("1234");

        var other = new Pbkdf2PinHasher(RandomNumberGenerator.GetBytes(32));

        Assert.False(other.Verify("1234", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$abc$AA==$AA==")]
    [InlineData("pbkdf2-sha256$600000$%%%$AA==")]
    [InlineData("md5$600000$AA==$AA==")]
    public void A_missing_or_malformed_hash_never_verifies(string? hash)
    {
        var hasher = new Pbkdf2PinHasher(Pepper);

        Assert.False(hasher.Verify("1234", hash));
    }

    [Fact]
    public void A_weak_pepper_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new Pbkdf2PinHasher(new byte[Pbkdf2PinHasher.MinimumPepperBytes - 1]));
    }
}
