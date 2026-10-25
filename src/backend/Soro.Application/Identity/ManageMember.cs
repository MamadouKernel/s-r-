using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Ce que les trois actions sur un membre (nouveau PIN, réactivation, changement de profil) vérifient avant tout : qui agit sur qui.</summary>
internal static class MemberAccess
{
    public static async Task<Result<User>> LoadManageableAsync(
        ICurrentUser actor,
        IUserRepository users,
        UserId id,
        CancellationToken cancellationToken)
    {
        if (RolePolicy.AssignableRoles(actor.Role).Count == 0)
        {
            return Result<User>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas gérer l'équipe."));
        }

        var target = await users.GetAsync(id, actor.Tenant, cancellationToken);
        if (target is null)
        {
            return Result<User>.Failure(ApplicationError.NotFound("Utilisateur introuvable."));
        }

        if (target.Id == actor.User)
        {
            return Result<User>.Failure(ApplicationError.Forbidden("Vous ne pouvez pas faire cela sur votre propre compte."));
        }

        return RolePolicy.CanManageRole(actor.Role, target.Role)
            ? Result<User>.Success(target)
            : Result<User>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas gérer cet utilisateur."));
    }

    public static ApplicationError InvalidPin { get; } =
        ApplicationError.RuleViolation($"Le code PIN doit comporter {PinPolicy.MinLength} à {PinPolicy.MaxLength} chiffres.");
}

public sealed record ResetUserPinCommand(UserId UserId, string Pin);

/// <summary>
/// Nouveau code PIN d'un membre qui a oublié le sien ou dont le compte est verrouillé (docs/specs/equipe-gestion-complete EQP-01). Mêmes droits que
/// la création du profil. Le PIN n'est écrit nulle part (ni journal, ni réponse) ; ses sessions ouvertes prennent fin.
/// </summary>
public sealed class ResetUserPinHandler(
    ICurrentUser actor,
    IUserRepository users,
    IPinHasher hasher,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<UserId>> HandleAsync(ResetUserPinCommand command, CancellationToken cancellationToken = default)
    {
        var loaded = await MemberAccess.LoadManageableAsync(actor, users, command.UserId, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return Result<UserId>.Failure(loaded.Error!);
        }

        if (!PinPolicy.IsValid(command.Pin))
        {
            return Result<UserId>.Failure(MemberAccess.InvalidPin);
        }

        var target = loaded.Value!;
        var now = clock.UtcNow;
        try
        {
            target.ResetPin(hasher.Hash(command.Pin), now);
        }
        catch (DomainException exception)
        {
            return Result<UserId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await users.UpdateAsync(target, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(actor.Tenant, actor.User, AuditActions.UserPinReset, target.Id.Value, target.DisplayName, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<UserId>.Success(target.Id);
    }
}

public sealed record EnableUserCommand(UserId UserId, string Pin);

/// <summary>Réactivation d'un membre désactivé (EQP-02), avec un nouveau PIN : l'ancien n'est jamais remis en service.</summary>
public sealed class EnableUserHandler(
    ICurrentUser actor,
    IUserRepository users,
    IPinHasher hasher,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<UserId>> HandleAsync(EnableUserCommand command, CancellationToken cancellationToken = default)
    {
        var loaded = await MemberAccess.LoadManageableAsync(actor, users, command.UserId, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return Result<UserId>.Failure(loaded.Error!);
        }

        if (!PinPolicy.IsValid(command.Pin))
        {
            return Result<UserId>.Failure(MemberAccess.InvalidPin);
        }

        var target = loaded.Value!;
        var now = clock.UtcNow;
        try
        {
            target.Reactivate(hasher.Hash(command.Pin), now);
        }
        catch (DomainException exception)
        {
            return Result<UserId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await users.UpdateAsync(target, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(actor.Tenant, actor.User, AuditActions.UserEnabled, target.Id.Value, target.DisplayName, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<UserId>.Success(target.Id);
    }
}

public sealed record ChangeUserRoleCommand(UserId UserId, Role Role);

/// <summary>
/// Changement de profil d'un membre actif (EQP-03). Il faut pouvoir gérer à la fois son profil actuel et le nouveau : le propriétaire passe un
/// membre à tout profil sauf propriétaire, le gérant n'échange que caissier et magasinier. Les sessions ouvertes prennent fin.
/// </summary>
public sealed class ChangeUserRoleHandler(
    ICurrentUser actor,
    IUserRepository users,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<UserId>> HandleAsync(ChangeUserRoleCommand command, CancellationToken cancellationToken = default)
    {
        var loaded = await MemberAccess.LoadManageableAsync(actor, users, command.UserId, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return Result<UserId>.Failure(loaded.Error!);
        }

        if (!RolePolicy.CanManageRole(actor.Role, command.Role))
        {
            return Result<UserId>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas donner ce rôle."));
        }

        var target = loaded.Value!;
        var now = clock.UtcNow;
        var before = target.Role;
        try
        {
            target.ChangeRole(command.Role, now);
        }
        catch (DomainException exception)
        {
            return Result<UserId>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await users.UpdateAsync(target, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(
                actor.Tenant,
                actor.User,
                AuditActions.UserRoleChanged,
                target.Id.Value,
                $"{target.DisplayName} : {RoleLabels.French(before)} → {RoleLabels.French(command.Role)}",
                now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<UserId>.Success(target.Id);
    }
}

/// <summary>
/// Cette session est-elle encore valable (EQP-05) ? Non si le membre est inconnu ou désactivé, ou si son PIN, son profil ou son état a changé
/// depuis l'émission du jeton. Le propriétaire (mot de passe) n'est concerné que s'il est désactivé, ce qui n'arrive pas.
/// </summary>
public sealed class CheckSessionHandler(IUserRepository users)
{
    public async Task<bool> HandleAsync(TenantId tenant, UserId user, DateTimeOffset issuedAt, CancellationToken cancellationToken = default)
    {
        var found = await users.GetAsync(user, tenant, cancellationToken);
        return found is { Status: UserStatus.Active } && !found.AreSessionsOutdated(issuedAt);
    }
}
