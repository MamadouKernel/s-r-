using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Cash;

/// <summary>
/// Session de caisse : racine d'agrégat qui porte le fond de caisse, les espèces encaissées
/// et la clôture. Son état ne change que par des opérations métier nommées.
/// </summary>
public sealed class CashRegisterSession
{
    private CashRegisterSession(
        SessionId id,
        RegisterAssignment assignment,
        Money openingFloat,
        DateTimeOffset openedAt)
    {
        Id = id;
        Assignment = assignment;
        OpeningFloat = openingFloat;
        OpenedAt = openedAt;
        Status = SessionStatus.Open;
    }

    public SessionId Id { get; }

    public RegisterAssignment Assignment { get; }

    public Money OpeningFloat { get; }

    public DateTimeOffset OpenedAt { get; }

    public SessionStatus Status { get; private set; }

    /// <summary>Espèces encaissées pendant la session, monnaie rendue déduite.</summary>
    public Money CashReceived { get; private set; }

    public Money? CountedCash { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Écart de clôture en FCFA : négatif = manque, positif = excédent (RG-07).</summary>
    public long? CashVariance { get; private set; }

    /// <summary>Montant attendu en caisse : fond + espèces encaissées (RG-24, hors dépenses pour l'instant).</summary>
    public Money ExpectedCash => OpeningFloat + CashReceived;

    public static CashRegisterSession Open(
        SessionId id,
        RegisterAssignment assignment,
        Money openingFloat,
        DateTimeOffset openedAt) =>
        new(id, assignment, openingFloat, openedAt);

    public Sale RecordSale(SaleDraft draft)
    {
        EnsureOpen();
        Ensure.That(draft.At >= OpenedAt, "Une vente ne peut pas précéder l'ouverture de la session.");

        var sale = Sale.Record(Id, Assignment, draft);
        CashReceived += sale.CashReceived;
        return sale;
    }

    public void Close(Money countedCash, DateTimeOffset at)
    {
        EnsureOpen();
        Ensure.That(at >= OpenedAt, "La clôture ne peut pas précéder l'ouverture de la session.");

        CountedCash = countedCash;
        ClosedAt = at;
        CashVariance = countedCash.Amount - ExpectedCash.Amount;
        Status = SessionStatus.Closed;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static CashRegisterSession Restore(
        SessionId id,
        RegisterAssignment assignment,
        Money openingFloat,
        DateTimeOffset openedAt,
        SessionStatus status,
        Money cashReceived,
        Money? countedCash,
        DateTimeOffset? closedAt,
        long? cashVariance) =>
        new(id, assignment, openingFloat, openedAt)
        {
            Status = status,
            CashReceived = cashReceived,
            CountedCash = countedCash,
            ClosedAt = closedAt,
            CashVariance = cashVariance,
        };

    private void EnsureOpen() =>
        Ensure.That(Status == SessionStatus.Open, "La session de caisse est clôturée (RG-01).");
}
