using Microsoft.EntityFrameworkCore;
using Soro.Application.Subscription;
using Soro.Domain.Common;
using Soro.Domain.Subscription;
using Soro.Domain.Tenancy;

namespace Soro.Infrastructure.Persistence;

internal static class SubscriptionMapper
{
    public static SubscriptionPayment ToDomain(SubscriptionPaymentRow row) =>
        SubscriptionPayment.Restore(
            new SubscriptionPaymentId(row.Id),
            new TenantId(row.TenantId),
            row.Period,
            row.Method,
            row.Reference,
            row.ReferenceKey,
            row.Amount,
            row.DeclaredAt,
            row.Status,
            row.DecidedAt,
            row.Reason,
            row.CoveredFrom,
            row.CoveredTo);

    public static void Apply(SubscriptionPaymentRow row, SubscriptionPayment payment)
    {
        row.Status = payment.Status;
        row.DecidedAt = payment.DecidedAt?.ToUniversalTime();
        row.Reason = payment.Reason;
        row.CoveredFrom = payment.CoveredFrom?.ToUniversalTime();
        row.CoveredTo = payment.CoveredTo?.ToUniversalTime();
    }
}

/// <summary>Paiements d'abonnement du commerce courant, sous l'isolation par commerce.</summary>
public sealed class SubscriptionPaymentRepository(SoroDbContext db) : ISubscriptionPaymentRepository
{
    public async Task<SubscriptionPayment?> GetAsync(SubscriptionPaymentId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.SubscriptionPayments.AsNoTracking()
            .SingleOrDefaultAsync(payment => payment.Id == id.Value && payment.TenantId == tenant.Value, cancellationToken);
        return row is null ? null : SubscriptionMapper.ToDomain(row);
    }

    public async Task<IReadOnlyList<SubscriptionPayment>> ListAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var rows = await db.SubscriptionPayments.AsNoTracking()
            .Where(payment => payment.TenantId == tenant.Value)
            .OrderByDescending(payment => payment.DeclaredAt)
            .ToListAsync(cancellationToken);
        return rows.Select(SubscriptionMapper.ToDomain).ToArray();
    }

    public Task AddAsync(SubscriptionPayment payment, CancellationToken cancellationToken)
    {
        db.SubscriptionPayments.Add(new SubscriptionPaymentRow
        {
            Id = payment.Id.Value,
            TenantId = payment.Tenant.Value,
            Period = payment.Period,
            Method = payment.Method,
            Reference = payment.Reference,
            ReferenceKey = payment.ReferenceKey,
            Amount = payment.AmountFcfa,
            DeclaredAt = payment.DeclaredAt.ToUniversalTime(),
            Status = payment.Status,
        });
        db.SubscriptionReferences.Add(new SubscriptionReferenceRow { Method = payment.Method, ReferenceKey = payment.ReferenceKey });
        return Task.CompletedTask;
    }

    public Task<bool> IsReferenceUsedAsync(SubscriptionMethod method, string referenceKey, CancellationToken cancellationToken) =>
        db.SubscriptionReferences.AnyAsync(row => row.Method == method && row.ReferenceKey == referenceKey, cancellationToken);
}

/// <summary>
/// Accès à tous les commerces pour l'équipe Sôrô. À n'utiliser qu'avec une connexion qui contourne l'isolation par commerce (outil d'exploitation) ;
/// avec le rôle de l'API, l'isolation ne montre que le commerce courant, ce qui est voulu.
/// </summary>
public sealed class PlatformSubscriptions(SoroDbContext db) : IPlatformSubscriptions
{
    public async Task<IReadOnlyList<PlatformPayment>> ListPendingAsync(CancellationToken cancellationToken)
    {
        var payments = await db.SubscriptionPayments.AsNoTracking()
            .Where(payment => payment.Status == SubscriptionPaymentStatus.Pending)
            .OrderBy(payment => payment.DeclaredAt)
            .ToListAsync(cancellationToken);
        var ids = payments.Select(payment => payment.TenantId).Distinct().ToArray();
        var commerces = await db.Commerces.AsNoTracking().Where(commerce => ids.Contains(commerce.Id)).ToDictionaryAsync(commerce => commerce.Id, cancellationToken);
        return payments
            .Where(payment => commerces.ContainsKey(payment.TenantId))
            .Select(payment => new PlatformPayment(SubscriptionMapper.ToDomain(payment), CommerceMapper.ToDomain(commerces[payment.TenantId])))
            .ToArray();
    }

    public async Task<PlatformPayment?> FindPaymentAsync(SubscriptionPaymentId id, CancellationToken cancellationToken)
    {
        var payment = await db.SubscriptionPayments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id.Value, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        var commerce = await db.Commerces.AsNoTracking().SingleOrDefaultAsync(row => row.Id == payment.TenantId, cancellationToken);
        return commerce is null ? null : new PlatformPayment(SubscriptionMapper.ToDomain(payment), CommerceMapper.ToDomain(commerce));
    }

    public async Task SaveDecisionAsync(SubscriptionPayment payment, Commerce commerce, CancellationToken cancellationToken)
    {
        var paymentRow = await db.SubscriptionPayments.SingleAsync(row => row.Id == payment.Id.Value, cancellationToken);
        SubscriptionMapper.Apply(paymentRow, payment);
        var commerceRow = await db.Commerces.SingleAsync(row => row.Id == commerce.Id.Value, cancellationToken);
        commerceRow.PaidUntil = commerce.PaidUntil?.ToUniversalTime();
    }

    public async Task<IReadOnlyList<Commerce>> ListCommercesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Commerces.AsNoTracking().OrderBy(commerce => commerce.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(CommerceMapper.ToDomain).ToArray();
    }

    public Task<bool> WasReminderSentAsync(TenantId tenant, DateTimeOffset endsAt, string kind, CancellationToken cancellationToken) =>
        db.SubscriptionReminders.AnyAsync(
            row => row.TenantId == tenant.Value && row.EndsAt == endsAt.ToUniversalTime() && row.Kind == kind, cancellationToken);

    public Task RecordReminderAsync(TenantId tenant, DateTimeOffset endsAt, string kind, DateTimeOffset at, CancellationToken cancellationToken)
    {
        db.SubscriptionReminders.Add(new SubscriptionReminderRow { TenantId = tenant.Value, EndsAt = endsAt.ToUniversalTime(), Kind = kind, SentAt = at.ToUniversalTime() });
        return Task.CompletedTask;
    }
}
