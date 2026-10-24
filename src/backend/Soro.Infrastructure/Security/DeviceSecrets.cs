using System.Security.Cryptography;
using System.Text;
using Soro.Application.Devices;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Secrets d'invitation et d'appareil : 256 bits tirés avec un générateur cryptographique, encodés en Base64 « URL » (43 caractères,
/// sans caractère à protéger dans une adresse). Ils ont assez d'entropie pour qu'un HMAC-SHA256 avec le poivre du serveur suffise comme
/// condensat (un PBKDF2 lent est inutile ici, il l'est pour un PIN de 4 chiffres). Le condensat est lié à son propriétaire.
/// </summary>
public sealed class DeviceSecrets : IDeviceSecrets
{
    private readonly byte[] _pepper;

    public DeviceSecrets(byte[] pepper)
    {
        ArgumentNullException.ThrowIfNull(pepper);
        if (pepper.Length < Pbkdf2PinHasher.MinimumPepperBytes)
        {
            throw new ArgumentException($"Le poivre doit contenir au moins {Pbkdf2PinHasher.MinimumPepperBytes} octets.", nameof(pepper));
        }

        _pepper = (byte[])pepper.Clone();
    }

    public string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public string Hash(Guid ownerId, string secret) =>
        Convert.ToBase64String(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"device:{ownerId:N}:{secret}")));

    public bool Verify(Guid ownerId, string secret, string? expectedHash)
    {
        var actual = Encoding.UTF8.GetBytes(Hash(ownerId, secret ?? string.Empty));
        var expected = Encoding.UTF8.GetBytes(expectedHash ?? new string('0', actual.Length));
        var equal = CryptographicOperations.FixedTimeEquals(actual, expected);
        return equal && expectedHash is not null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
