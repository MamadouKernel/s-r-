using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Soro.Application.Sales;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Security;

/// <summary>
/// Approbation d'un gérant signée par le serveur (HMAC-SHA256, clé dérivée du poivre) : commerce, gérant, vente, usage et fin de validité sont dans la
/// preuve, qui ne vaut donc que pour cette vente et cet usage. Elle remplace le PIN dans la file d'envoi de la caisse (docs/decisions/0020).
/// </summary>
public sealed class HmacApprovalSeal : IApprovalSeal
{
    private readonly byte[] _key;

    public HmacApprovalSeal(byte[] pepper)
    {
        if (pepper.Length < Pbkdf2PinHasher.MinimumPepperBytes)
        {
            throw new ArgumentException($"Le poivre doit contenir au moins {Pbkdf2PinHasher.MinimumPepperBytes} octets.", nameof(pepper));
        }

        // Une clé propre à cet usage : le même poivre ne signe jamais deux sortes de preuves avec la même clé.
        _key = HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes("soro:approval-seal:v1"));
    }

    public string Issue(TenantId tenant, UserId approver, Guid subject, string purpose, DateTimeOffset expiresAt)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('|', tenant.Value, approver.Value, subject, purpose, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        return Base64Url(payload) + "." + Base64Url(HMACSHA256.HashData(_key, payload));
    }

    public UserId? Open(string token, TenantId tenant, Guid subject, string purpose, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 2 || !TryDecode(parts[0], out var payload) || !TryDecode(parts[1], out var mac))
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), mac))
        {
            return null;
        }

        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 5
            || !Guid.TryParse(fields[0], out var tokenTenant) || !Guid.TryParse(fields[1], out var approver) || !Guid.TryParse(fields[2], out var tokenSubject)
            || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            return null;
        }

        var valid = tokenTenant == tenant.Value && tokenSubject == subject && fields[3] == purpose && now.ToUnixTimeSeconds() <= expires;
        return valid ? new UserId(approver) : null;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string text, out byte[] bytes)
    {
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            bytes = Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
