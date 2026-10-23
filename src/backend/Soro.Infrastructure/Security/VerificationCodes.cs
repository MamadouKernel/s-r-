using System.Security.Cryptography;
using System.Text;
using Soro.Application.Signup;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Codes à 6 chiffres tirés avec un générateur cryptographique. Le code n'est jamais stocké : seul son HMAC-SHA256 l'est, avec le poivre
/// du serveur et l'identifiant de la demande (un condensat ne vaut donc que pour sa demande). Comparaison en temps constant.
/// Un code de 10⁶ valeurs n'est protégé que par les plafonds d'essais et de durée du domaine (5 essais, 10 minutes).
/// </summary>
public sealed class VerificationCodes : IVerificationCodes
{
    private readonly byte[] _pepper;

    public VerificationCodes(byte[] pepper)
    {
        ArgumentNullException.ThrowIfNull(pepper);
        if (pepper.Length < Pbkdf2PinHasher.MinimumPepperBytes)
        {
            throw new ArgumentException($"Le poivre doit contenir au moins {Pbkdf2PinHasher.MinimumPepperBytes} octets.", nameof(pepper));
        }

        _pepper = (byte[])pepper.Clone();
    }

    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public string Hash(Guid verificationId, string code) =>
        Convert.ToBase64String(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"{verificationId:N}:{code}")));

    public bool Verify(Guid verificationId, string code, string expectedHash)
    {
        var actual = Encoding.UTF8.GetBytes(Hash(verificationId, code));
        var expected = Encoding.UTF8.GetBytes(expectedHash ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
