using Soro.Domain.Common;
using Soro.Domain.Subscription;
using Soro.Domain.Tenancy;

namespace Soro.Application.Subscription;

/// <summary>Paiements d'abonnement du commerce courant (isolation par commerce).</summary>
public interface ISubscriptionPaymentRepository
{
    Task<SubscriptionPayment?> GetAsync(SubscriptionPaymentId id, TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<SubscriptionPayment>> ListAsync(TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Enregistre aussi la référence, qui ne sert qu'une fois par moyen, tous commerces confondus.</summary>
    Task AddAsync(SubscriptionPayment payment, CancellationToken cancellationToken);

    /// <summary>Une référence est unique par moyen sur toute la plateforme (une fraude à la référence se voit entre commerces).</summary>
    Task<bool> IsReferenceUsedAsync(SubscriptionMethod method, string referenceKey, CancellationToken cancellationToken);
}

/// <summary>Réglages d'abonnement : prix des deux périodes et numéros de Sôrô où payer (docs/decisions/0011).</summary>
public interface ISubscriptionSettings
{
    long MonthlyPriceFcfa { get; }

    long AnnualPriceFcfa { get; }

    IReadOnlyDictionary<SubscriptionMethod, string> PayNumbers { get; }
}

public sealed record PlatformPayment(SubscriptionPayment Payment, Commerce Commerce);

/// <summary>
/// Accès de l'équipe Sôrô à tous les commerces. Réservé à l'outil d'exploitation, qui se connecte avec le rôle propriétaire de la base : l'API des
/// commerces ne l'utilise jamais (isolation RG-15).
/// </summary>
public interface IPlatformSubscriptions
{
    Task<IReadOnlyList<PlatformPayment>> ListPendingAsync(CancellationToken cancellationToken);

    Task<PlatformPayment?> FindPaymentAsync(SubscriptionPaymentId id, CancellationToken cancellationToken);

    /// <summary>Enregistre la décision sur le paiement et, s'il y a lieu, la nouvelle fin de période du commerce.</summary>
    Task SaveDecisionAsync(SubscriptionPayment payment, Commerce commerce, CancellationToken cancellationToken);

    Task<IReadOnlyList<Commerce>> ListCommercesAsync(CancellationToken cancellationToken);

    Task<bool> WasReminderSentAsync(TenantId tenant, DateTimeOffset endsAt, string kind, CancellationToken cancellationToken);

    Task RecordReminderAsync(TenantId tenant, DateTimeOffset endsAt, string kind, DateTimeOffset at, CancellationToken cancellationToken);
}
