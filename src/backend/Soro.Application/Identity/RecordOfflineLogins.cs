using System.Globalization;
using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Une entrée sans réseau, notée sur l'appareil : l'identifiant de l'événement sert à ne l'inscrire qu'une fois (docs/specs/connexion-hors-ligne HLC-10).</summary>
public sealed record OfflineLoginInput(Guid EventId, UserId User, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record RecordOfflineLoginsCommand(IReadOnlyList<OfflineLoginInput> Logins);

/// <summary>
/// Inscrit au journal (rubrique Sécurité) les connexions faites sans réseau, envoyées avec la file au retour du réseau. Les heures sont celles de
/// l'appareil : une heure absurde est signalée, pas corrigée. Seuls les comptes du commerce qui peuvent entrer sans réseau (caissier, gérant)
/// comptent ; les autres sont ignorés. Rejouable : un événement déjà inscrit ne l'est pas deux fois.
/// </summary>
public sealed class RecordOfflineLoginsHandler(ICurrentUser user, IUserDirectory directory, IAuditTrail audit, IClock clock, IUnitOfWork unitOfWork)
{
    public const int MaxPerCall = 100;
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

    /// <returns>Le nombre d'événements inscrits cette fois.</returns>
    public async Task<Result<int>> HandleAsync(RecordOfflineLoginsCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<int>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas envoyer ces informations."));
        }

        if (command.Logins.Count > MaxPerCall)
        {
            return Result<int>.Failure(ApplicationError.RuleViolation($"Envoyez au plus {MaxPerCall} connexions à la fois."));
        }

        var now = clock.UtcNow;
        var recorded = 0;
        foreach (var login in command.Logins)
        {
            var role = await directory.GetRoleAsync(login.User, user.Tenant, cancellationToken);
            if (role is not (Role.Cashier or Role.Manager) || await audit.ExistsAsync(AuditActions.LoginOffline, login.EventId, cancellationToken))
            {
                continue;
            }

            await audit.RecordAsync(new AuditEntry(user.Tenant, login.User, AuditActions.LoginOffline, login.EventId, Describe(login, now), now), cancellationToken);
            recorded++;
        }

        if (recorded > 0)
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Result<int>.Success(recorded);
    }

    private static string Describe(OfflineLoginInput login, DateTimeOffset now)
    {
        var day = login.StartedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var from = login.StartedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var detail = login.EndedAt is { } end
            ? $"Connexion sans réseau le {day}, de {from} à {end.ToString("HH:mm", CultureInfo.InvariantCulture)}."
            : $"Connexion sans réseau le {day}, à partir de {from}.";
        var suspect = login.StartedAt > now + ClockTolerance || (login.EndedAt is { } finished && (finished < login.StartedAt || finished > now + ClockTolerance));
        return suspect ? detail + " L'heure de l'appareil semble fausse." : detail;
    }
}
