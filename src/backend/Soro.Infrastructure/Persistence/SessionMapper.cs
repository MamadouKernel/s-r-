using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

internal static class SessionMapper
{
    public static SessionRow ToRow(CashRegisterSession session)
    {
        var row = new SessionRow
        {
            Id = session.Id.Value,
            TenantId = session.Assignment.Tenant.Value,
            PointOfSaleId = session.Assignment.PointOfSale.Value,
            RegisterId = session.Assignment.Register.Value,
            CashierId = session.Assignment.Cashier.Value,
            OpeningFloat = session.OpeningFloat.Amount,
            OpenedAt = session.OpenedAt.ToUniversalTime(),
        };
        Apply(row, session);
        return row;
    }

    /// <summary>Reporte l'état changeant de la session (les champs d'attribution ne changent jamais).</summary>
    public static void Apply(SessionRow row, CashRegisterSession session)
    {
        row.Status = session.Status;
        row.CashReceived = session.CashReceived.Amount;
        row.CashRefunded = session.CashRefunded.Amount;
        row.CountedCash = session.CountedCash?.Amount;
        row.ClosedAt = session.ClosedAt?.ToUniversalTime();
        row.CashVariance = session.CashVariance;
        row.CashPaidOut = session.CashPaidOut.Amount;
        row.CashPaidIn = session.CashPaidIn.Amount;
        row.FirstCount = session.FirstCount?.Amount;
        row.FirstCountDetail = session.FirstCountDetail;
        row.CountDetail = session.CountDetail;
    }

    public static CashRegisterSession ToDomain(SessionRow row) =>
        CashRegisterSession.Restore(
            new SessionId(row.Id),
            new RegisterAssignment(
                new TenantId(row.TenantId),
                new PointOfSaleId(row.PointOfSaleId),
                new RegisterId(row.RegisterId),
                new UserId(row.CashierId)),
            Money.Fcfa(row.OpeningFloat),
            row.OpenedAt,
            row.Status,
            Money.Fcfa(row.CashReceived),
            row.CountedCash is { } counted ? Money.Fcfa(counted) : null,
            row.ClosedAt,
            row.CashVariance,
            Money.Fcfa(row.CashRefunded),
            Money.Fcfa(row.CashPaidOut),
            Money.Fcfa(row.CashPaidIn),
            row.FirstCount is { } first ? Money.Fcfa(first) : null,
            row.FirstCountDetail,
            row.CountDetail);
}
