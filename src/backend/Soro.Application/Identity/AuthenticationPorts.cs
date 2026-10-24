using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Toutes les lectures reçoivent le commerce : l'isolation est appliquée à la source (RG-15).</summary>
public interface IUserRepository
{
    Task<User?> GetAsync(UserId id, TenantId tenant, CancellationToken cancellationToken);

    /// <param name="tenant">Commerce de l'utilisateur recherché.</param>
    /// <param name="identifier">Identifiant déjà normalisé (<see cref="User.Normalize"/>).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<User?> FindByIdentifierAsync(TenantId tenant, string identifier, CancellationToken cancellationToken);

    /// <summary>Tous les utilisateurs du commerce, actifs ou non.</summary>
    Task<IReadOnlyList<User>> ListAsync(TenantId tenant, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task UpdateAsync(User user, CancellationToken cancellationToken);
}

/// <summary>Hachage lent et salé des codes PIN. Le PIN n'est jamais stocké ni journalisé.</summary>
public interface IPinHasher
{
    string Hash(string pin);

    /// <summary>
    /// Compare en temps constant. Avec un condensat <c>null</c> (compte inexistant), fait quand même le même travail
    /// et renvoie <c>false</c> : on ne devine pas l'existence d'un compte au temps de réponse.
    /// </summary>
    bool Verify(string pin, string? hash);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Émet le jeton d'accès porté ensuite à chaque requête (commerce, utilisateur, profil).</summary>
public interface ITokenIssuer
{
    AccessToken Issue(TenantId tenant, UserId user, Role role, DateTimeOffset now, DeviceId? device = null);
}

/// <summary>
/// Fixe le commerce de la requête quand il n'est pas encore connu par un jeton (la connexion). Sans cela,
/// l'isolation par commerce de la base ne laisserait voir aucune ligne.
/// </summary>
public interface ITenantScope
{
    void Enter(TenantId tenant);
}

/// <summary>
/// Hachage lent et salé des mots de passe des propriétaires. Même algorithme et mêmes garanties que le PIN (secret du serveur,
/// PBKDF2, comparaison en temps constant, même travail pour un compte inexistant).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string? hash);
}
