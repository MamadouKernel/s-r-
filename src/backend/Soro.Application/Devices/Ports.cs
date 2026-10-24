using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Application.Devices;

public interface IDeviceRepository
{
    Task<Device?> GetAsync(DeviceId id, TenantId tenant, CancellationToken cancellationToken);

    Task<IReadOnlyList<Device>> ListAsync(TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(Device device, CancellationToken cancellationToken);

    Task UpdateAsync(Device device, CancellationToken cancellationToken);
}

public interface IDeviceInvitationRepository
{
    Task<DeviceInvitation?> GetAsync(DeviceInvitationId id, TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(DeviceInvitation invitation, CancellationToken cancellationToken);

    Task UpdateAsync(DeviceInvitation invitation, CancellationToken cancellationToken);
}

/// <summary>Secrets aléatoires (invitation, appareil) : tirage sûr, condensat lié à son propriétaire, comparaison en temps constant.</summary>
public interface IDeviceSecrets
{
    /// <summary>Un secret de 256 bits, encodé en texte sans caractère à protéger dans une adresse.</summary>
    string NewSecret();

    string Hash(Guid ownerId, string secret);

    /// <summary>Même travail de calcul pour un condensat absent : on ne devine pas l'existence d'un appareil au temps de réponse.</summary>
    bool Verify(Guid ownerId, string secret, string? expectedHash);
}

/// <summary>Réglage : la connexion par PIN exige-t-elle un appareil enrôlé ? Vrai en production (ADR 0006).</summary>
public interface IDevicePolicy
{
    bool Required { get; }
}

/// <summary>Ce qu'un appareil présente à la connexion : son identifiant et son secret, retenus à l'enrôlement.</summary>
public sealed record DeviceCredentials(DeviceId Device, string Secret);

/// <summary>
/// Jeton d'invitation : commerce, invitation et secret mis bout à bout. Les deux premiers ne sont pas secrets (ils servent à retrouver l'invitation
/// sous l'isolation par commerce) ; seul le secret protège l'invitation.
/// </summary>
public static class EnrollmentToken
{
    private const int GuidLength = 32;

    public static string Format(TenantId tenant, DeviceInvitationId invitation, string secret) =>
        $"{tenant.Value:N}{invitation.Value:N}{secret}";

    public static bool TryParse(string? token, out TenantId tenant, out DeviceInvitationId invitation, out string secret)
    {
        tenant = default;
        invitation = default;
        secret = string.Empty;
        var text = (token ?? string.Empty).Trim();
        if (text.Length <= GuidLength * 2
            || !Guid.TryParseExact(text[..GuidLength], "N", out var tenantGuid)
            || !Guid.TryParseExact(text[GuidLength..(GuidLength * 2)], "N", out var invitationGuid)
            || tenantGuid == Guid.Empty)
        {
            return false;
        }

        tenant = new TenantId(tenantGuid);
        invitation = new DeviceInvitationId(invitationGuid);
        secret = text[(GuidLength * 2)..];
        return true;
    }
}
