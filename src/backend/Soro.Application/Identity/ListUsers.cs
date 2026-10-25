using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>
/// Un membre de l'équipe tel que l'écran Équipe le montre. Construit à part de l'entité : le condensat du PIN n'a aucun
/// chemin vers une réponse. <see cref="CanDisable"/> est calculé ici, avec la même règle que la désactivation.
/// </summary>
public sealed record UserView(
    Guid Id,
    string Identifier,
    string DisplayName,
    Role Role,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LockedUntil,
    bool CanDisable,
    bool IsSelf,
    bool CanRenewCode,
    bool CanEnable,
    bool CanChangeRole);

public sealed record TeamView(IReadOnlyList<UserView> Users, IReadOnlyList<Role> AssignableRoles);

/// <summary>Lecture de l'équipe (EQ-01) : propriétaire et gérant, dans leur commerce.</summary>
public sealed class ListUsersHandler(ICurrentUser actor, IUserRepository users, IClock clock)
{
    public async Task<Result<TeamView>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanListTeam(actor.Role))
        {
            return Result<TeamView>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas consulter l'équipe."));
        }

        var now = clock.UtcNow;
        var found = await users.ListAsync(actor.Tenant, cancellationToken);
        var views = found
            .OrderBy(user => user.Status == UserStatus.Active ? 0 : 1)
            .ThenBy(user => user.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(user => user.Identifier, StringComparer.Ordinal)
            .Select(user => new UserView(
                user.Id.Value,
                user.Identifier,
                user.DisplayName,
                user.Role,
                user.Status,
                user.CreatedAt,
                user.LastLoginAt,
                user.IsLocked(now) ? user.LockedUntil : null,
                CanDisable: user.Status == UserStatus.Active && user.Id != actor.User && RolePolicy.CanManageRole(actor.Role, user.Role),
                IsSelf: user.Id == actor.User,
                CanRenewCode: CanAct(user, UserStatus.Active),
                CanEnable: CanAct(user, UserStatus.Disabled),
                CanChangeRole: CanAct(user, UserStatus.Active)))
            .ToArray();
        return Result<TeamView>.Success(new TeamView(views, RolePolicy.AssignableRoles(actor.Role)));

        bool CanAct(User user, UserStatus status) =>
            user.Status == status && user.Id != actor.User && RolePolicy.CanManageRole(actor.Role, user.Role);
    }
}

public sealed record DirectoryEntry(Guid Id, string DisplayName);

/// <summary>
/// Annuaire minimal pour nommer les auteurs de l'historique du stock (EQ-02) : identifiant et nom, actifs ou non
/// puisqu'un ancien employé reste l'auteur de mouvements passés.
/// </summary>
public sealed class GetDirectoryHandler(ICurrentUser actor, IUserRepository users)
{
    public async Task<Result<IReadOnlyList<DirectoryEntry>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanReadDirectory(actor.Role))
        {
            return Result<IReadOnlyList<DirectoryEntry>>.Failure(
                ApplicationError.Forbidden("Ce profil ne peut pas consulter l'annuaire."));
        }

        var found = await users.ListAsync(actor.Tenant, cancellationToken);
        return Result<IReadOnlyList<DirectoryEntry>>.Success(
            found.Select(user => new DirectoryEntry(user.Id.Value, user.DisplayName)).ToArray());
    }
}
