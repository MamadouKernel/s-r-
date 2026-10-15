using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

public sealed record RegisterUserCommand(UserId Id, string Identifier, string DisplayName, Role Role, string Pin);

public sealed record RegisterUserResult(UserId UserId, bool AlreadyProcessed);

/// <summary>
/// Création d'un utilisateur du commerce courant (écran Équipe). Le propriétaire crée tous les profils sauf un autre
/// propriétaire ; un gérant ne crée que des caissiers et des magasiniers. Rejouable par identifiant d'utilisateur.
/// </summary>
public sealed class RegisterUserHandler(
    ICurrentUser actor,
    IUserRepository users,
    IPinHasher hasher,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RegisterUserResult>> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageRole(actor.Role, command.Role))
        {
            return Result<RegisterUserResult>.Failure(
                ApplicationError.Forbidden("Ce profil ne peut pas créer un utilisateur avec ce rôle."));
        }

        if (!PinPolicy.IsValid(command.Pin))
        {
            return Result<RegisterUserResult>.Failure(
                ApplicationError.RuleViolation($"Le code PIN doit comporter {PinPolicy.MinLength} à {PinPolicy.MaxLength} chiffres."));
        }

        if (await users.GetAsync(command.Id, actor.Tenant, cancellationToken) is not null)
        {
            return Result<RegisterUserResult>.Success(new(command.Id, AlreadyProcessed: true));
        }

        if (await users.FindByIdentifierAsync(actor.Tenant, User.Normalize(command.Identifier), cancellationToken) is not null)
        {
            return Result<RegisterUserResult>.Failure(
                new ApplicationError(ErrorCodes.IdentifierTaken, "Cet identifiant est déjà utilisé dans ce commerce."));
        }

        try
        {
            var user = User.Register(
                command.Id,
                actor.Tenant,
                command.Identifier,
                command.DisplayName,
                command.Role,
                hasher.Hash(command.Pin),
                clock.UtcNow);

            await users.AddAsync(user, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<RegisterUserResult>.Success(new(command.Id, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<RegisterUserResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}
