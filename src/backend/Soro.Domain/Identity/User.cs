using Soro.Domain.Common;

namespace Soro.Domain.Identity;

public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>
/// Utilisateur d'un commerce (propriétaire, gérant, caissier…). Porte les règles de connexion qui ne dépendent
/// d'aucune technologie : identifiant valide, verrouillage après échecs répétés, désactivation.
/// Le code PIN n'existe ici que sous forme de condensat produit par l'infrastructure.
/// </summary>
public sealed class User
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private const int MinIdentifierLength = 3;
    private const int MaxIdentifierLength = 32;
    private const int MaxDisplayNameLength = 100;

    private User(
        UserId id,
        TenantId tenant,
        string identifier,
        string displayName,
        Role role,
        string pinHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        Tenant = tenant;
        Identifier = identifier;
        DisplayName = displayName;
        Role = role;
        PinHash = pinHash;
        CreatedAt = createdAt;
        Status = UserStatus.Active;
    }

    public UserId Id { get; }

    public TenantId Tenant { get; }

    /// <summary>Nom de connexion, en minuscules, unique dans le commerce.</summary>
    public string Identifier { get; }

    public string DisplayName { get; }

    public Role Role { get; }

    public string PinHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public UserStatus Status { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public static User Register(
        UserId id,
        TenantId tenant,
        string identifier,
        string displayName,
        Role role,
        string pinHash,
        DateTimeOffset at)
    {
        var normalized = NormalizeIdentifier(identifier);
        Ensure.That(!string.IsNullOrWhiteSpace(displayName), "Le nom de l'utilisateur est obligatoire.");
        Ensure.That(displayName.Trim().Length <= MaxDisplayNameLength, "Le nom de l'utilisateur est trop long.");
        Ensure.That(!string.IsNullOrWhiteSpace(pinHash), "Le condensat du code PIN est obligatoire.");

        return new User(id, tenant, normalized, displayName.Trim(), role, pinHash, at);
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && now < until;

    public bool CanLogIn(DateTimeOffset now) => Status == UserStatus.Active && !IsLocked(now);

    /// <summary>
    /// Compte un échec de connexion. Au <see cref="MaxFailedAttempts"/>e échec, le compte est verrouillé et le
    /// compteur repart de zéro. Un échec pendant le verrouillage ne le prolonge pas.
    /// </summary>
    public void RecordFailedAttempt(DateTimeOffset now)
    {
        if (Status != UserStatus.Active || IsLocked(now))
        {
            return;
        }

        FailedAttempts++;
        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntil = now + LockDuration;
            FailedAttempts = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        Ensure.That(CanLogIn(now), "Ce compte ne peut pas se connecter.");
        FailedAttempts = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    public void Disable() => Status = UserStatus.Disabled;

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static User Restore(
        UserId id,
        TenantId tenant,
        string identifier,
        string displayName,
        Role role,
        string pinHash,
        DateTimeOffset createdAt,
        UserStatus status,
        int failedAttempts,
        DateTimeOffset? lockedUntil,
        DateTimeOffset? lastLoginAt) =>
        new(id, tenant, identifier, displayName, role, pinHash, createdAt)
        {
            Status = status,
            FailedAttempts = failedAttempts,
            LockedUntil = lockedUntil,
            LastLoginAt = lastLoginAt,
        };

    /// <summary>Forme canonique d'un identifiant saisi : sans espaces autour, en minuscules.</summary>
    public static string Normalize(string? identifier) => (identifier ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeIdentifier(string identifier)
    {
        var normalized = Normalize(identifier);
        Ensure.That(
            normalized.Length is >= MinIdentifierLength and <= MaxIdentifierLength && normalized.All(IsAllowed)
                && char.IsLetterOrDigit(normalized[0]),
            "L'identifiant doit faire 3 à 32 caractères : lettres, chiffres, point, tiret ou souligné, "
            + "et commencer par une lettre ou un chiffre.");
        return normalized;
    }

    private static bool IsAllowed(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_';
}
