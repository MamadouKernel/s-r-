using System.Security.Cryptography;
using System.Text;
using Soro.Application.Identity;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Condensat des codes PIN : HMAC-SHA256 avec un poivre secret du serveur, puis PBKDF2-HMAC-SHA256 salé
/// (600 000 itérations). Un PIN de 4 à 6 chiffres n'a que 10⁴ à 10⁶ valeurs : sans le poivre, gardé hors de la
/// base, une fuite de la table ne suffirait pas à retrouver les codes, même hors ligne.
/// Format stocké : <c>pbkdf2-sha256$itérations$sel$clé</c> (Base64), ce qui permet d'augmenter le coût plus tard.
/// </summary>
public sealed class Pbkdf2PinHasher : IPinHasher
{
    public const int MinimumPepperBytes = 16;

    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private const int MaxIterations = 5_000_000;

    private static readonly byte[] DummySalt = new byte[SaltSize];

    private readonly byte[] _pepper;

    public Pbkdf2PinHasher(byte[] pepper)
    {
        ArgumentNullException.ThrowIfNull(pepper);
        if (pepper.Length < MinimumPepperBytes)
        {
            throw new ArgumentException($"Le poivre doit contenir au moins {MinimumPepperBytes} octets.", nameof(pepper));
        }

        _pepper = (byte[])pepper.Clone();
    }

    public string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Derive(pin, salt, Iterations);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string pin, string? hash)
    {
        if (!TryParse(hash, out var iterations, out var salt, out var expected))
        {
            Derive(pin, DummySalt, Iterations);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Derive(pin, salt, iterations), expected);
    }

    private static bool TryParse(string? hash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        var parts = hash?.Split('$');
        if (parts is not { Length: 4 }
            || parts[0] != Prefix
            || !int.TryParse(parts[1], out iterations)
            || iterations is < 1 or > MaxIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return key.Length == KeySize && salt.Length > 0;
    }

    private byte[] Derive(string pin, byte[] salt, int iterations)
    {
        var material = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(pin));
        return Rfc2898DeriveBytes.Pbkdf2(material, salt, iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
