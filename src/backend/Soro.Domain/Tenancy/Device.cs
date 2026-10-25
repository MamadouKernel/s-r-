using Soro.Domain.Common;

namespace Soro.Domain.Tenancy;

/// <summary>
/// Invitation à rattacher un appareil au commerce (ADR 0006) : à usage unique, valable 24 heures. Elle ne porte jamais le secret en clair,
/// seulement son condensat.
/// </summary>
public sealed class DeviceInvitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private DeviceInvitation(
        DeviceInvitationId id,
        TenantId tenant,
        string secretHash,
        UserId createdBy,
        string? suggestedName,
        DateTimeOffset createdAt)
    {
        Id = id;
        Tenant = tenant;
        SecretHash = secretHash;
        CreatedBy = createdBy;
        SuggestedName = suggestedName;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    public DeviceInvitationId Id { get; }

    public TenantId Tenant { get; }

    public string SecretHash { get; }

    public UserId CreatedBy { get; }

    /// <summary>Nom proposé pour l'appareil (« Caisse 1 »), facultatif.</summary>
    public string? SuggestedName { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    /// <summary>Date à laquelle le propriétaire ou le gérant a annulé le lien ; <c>null</c> tant qu'il n'est pas annulé.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static DeviceInvitation Issue(
        DeviceInvitationId id,
        TenantId tenant,
        string secretHash,
        UserId createdBy,
        string? suggestedName,
        DateTimeOffset now)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(secretHash), "Le condensat du secret est obligatoire.");
        return new DeviceInvitation(id, tenant, secretHash, createdBy, Device.NormalizeOptionalName(suggestedName), now);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsUsable(DateTimeOffset now) => !IsConsumed && !IsRevoked && !IsExpired(now);

    /// <summary>Lien encore en attente d'un appareil : ni utilisé, ni annulé, ni périmé.</summary>
    public bool IsPending(DateTimeOffset now) => IsUsable(now);

    /// <summary>Annule un lien qui n'a pas servi. Rejouable : la première date est conservée. Un lien déjà utilisé ne s'annule pas (on retire l'appareil).</summary>
    public void Revoke(DateTimeOffset now)
    {
        Ensure.That(!IsConsumed, "Ce lien a déjà été utilisé : retirez l'appareil depuis la liste des appareils.");
        RevokedAt ??= now;
    }

    public void Consume(DateTimeOffset now)
    {
        Ensure.That(IsUsable(now), "Cette invitation n'est plus utilisable.");
        ConsumedAt = now;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static DeviceInvitation Restore(
        DeviceInvitationId id,
        TenantId tenant,
        string secretHash,
        UserId createdBy,
        string? suggestedName,
        DateTimeOffset createdAt,
        DateTimeOffset? consumedAt,
        DateTimeOffset? revokedAt) =>
        new(id, tenant, secretHash, createdBy, suggestedName, createdAt) { ConsumedAt = consumedAt, RevokedAt = revokedAt };
}

/// <summary>
/// Un appareil rattaché au commerce (ADR 0006) : il se présente avec son secret (condensat en base) à chaque connexion et peut être révoqué,
/// ce qui coupe immédiatement ses accès.
/// </summary>
public sealed class Device
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 50;

    /// <summary>La dernière activité n'est réécrite que si elle a plus de cet âge : un appareil actif ne coûte pas une écriture par requête.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private Device(
        DeviceId id,
        TenantId tenant,
        string name,
        string secretHash,
        DeviceInvitationId invitation,
        DateTimeOffset enrolledAt)
    {
        Id = id;
        Tenant = tenant;
        Name = name;
        SecretHash = secretHash;
        Invitation = invitation;
        EnrolledAt = enrolledAt;
        LastSeenAt = enrolledAt;
    }

    public DeviceId Id { get; }

    public TenantId Tenant { get; }

    public string Name { get; }

    public string SecretHash { get; }

    public DeviceInvitationId Invitation { get; }

    public DateTimeOffset EnrolledAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static Device Enroll(
        DeviceId id,
        TenantId tenant,
        string name,
        string secretHash,
        DeviceInvitationId invitation,
        DateTimeOffset now)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(secretHash), "Le condensat du secret est obligatoire.");
        var trimmed = (name ?? string.Empty).Trim();
        Ensure.That(
            trimmed.Length is >= MinNameLength and <= MaxNameLength,
            $"Le nom de l'appareil doit faire {MinNameLength} à {MaxNameLength} caractères.");
        return new Device(id, tenant, trimmed, secretHash, invitation, now);
    }

    /// <summary>Idempotent : une seconde révocation garde la première (qui, quand).</summary>
    public void Revoke(UserId by, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        RevokedAt = now;
        RevokedBy = by;
    }

    /// <summary>Note une activité. Renvoie <c>true</c> si la valeur a changé et mérite une écriture.</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (!IsActive || now - LastSeenAt < TouchInterval)
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }

    internal static string? NormalizeOptionalName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var trimmed = name.Trim();
        Ensure.That(
            trimmed.Length is >= MinNameLength and <= MaxNameLength,
            $"Le nom de l'appareil doit faire {MinNameLength} à {MaxNameLength} caractères.");
        return trimmed;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static Device Restore(
        DeviceId id,
        TenantId tenant,
        string name,
        string secretHash,
        DeviceInvitationId invitation,
        DateTimeOffset enrolledAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? revokedAt,
        UserId? revokedBy) =>
        new(id, tenant, name, secretHash, invitation, enrolledAt)
        {
            LastSeenAt = lastSeenAt,
            RevokedAt = revokedAt,
            RevokedBy = revokedBy,
        };
}
