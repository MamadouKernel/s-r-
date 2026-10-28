using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Cash;

/// <summary>
/// Session de caisse : racine d'agrégat qui porte le fond de caisse, les espèces encaissées
/// et la clôture. Son état ne change que par des opérations métier nommées.
/// </summary>
public enum CountOutcome
{
    /// <summary>Le comptage est accepté : la session est clôturée.</summary>
    Closed,

    /// <summary>Le comptage s'écarte trop de ce qui est attendu : la session reste ouverte, un recomptage est demandé.</summary>
    NeedsRecount,
}

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

    /// <summary>Espèces rendues aux clients pendant la session (annulations et retours, docs/decisions/0020).</summary>
    public Money CashRefunded { get; private set; }

    /// <summary>Espèces sorties du tiroir en cours de journée : dépenses et versements (docs/decisions/0021).</summary>
    public Money CashPaidOut { get; private set; }

    /// <summary>Espèces ajoutées au tiroir en cours de journée (apports de monnaie).</summary>
    public Money CashPaidIn { get; private set; }

    /// <summary>Le premier comptage, gardé quand il s'écartait trop et qu'un recomptage a été demandé ; <c>null</c> sinon.</summary>
    public Money? FirstCount { get; private set; }

    /// <summary>Le détail par coupures du premier comptage et du comptage final (JSON, facultatif) : gardé tel quel pour l'audit.</summary>
    public string? FirstCountDetail { get; private set; }

    public string? CountDetail { get; private set; }

    public Money? CountedCash { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Écart de clôture en FCFA : négatif = manque, positif = excédent (RG-07).</summary>
    public long? CashVariance { get; private set; }

    /// <summary>Montant attendu en caisse : fond + espèces encaissées − espèces rendues − dépenses et versements + apports (RG-24).</summary>
    public Money ExpectedCash => OpeningFloat + CashReceived - CashRefunded - CashPaidOut + CashPaidIn;

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

    /// <summary>Des espèces sortent du tiroir pour rembourser un client : la session ouverte les déduit de ce qui est attendu.</summary>
    public void RecordCashRefund(Money amount)
    {
        EnsureOpen();
        Ensure.That(amount > Money.Zero, "Le montant remboursé doit être supérieur à zéro.");
        CashRefunded += amount;
    }

    /// <summary>Une dépense, un versement ou un apport. L'argent qui sort ne peut pas dépasser ce que le tiroir doit contenir.</summary>
    public void RecordCashMovement(CashMovementKind kind, Money amount)
    {
        EnsureOpen();
        Ensure.That(amount > Money.Zero, "Le montant doit être supérieur à zéro.");
        if (kind == CashMovementKind.Topup)
        {
            CashPaidIn += amount;
            return;
        }

        Ensure.That(amount <= ExpectedCash, "Cette sortie dépasse l'argent qui doit se trouver dans le tiroir.");
        CashPaidOut += amount;
    }

    /// <summary>
    /// Le comptage de clôture, à l'aveugle (docs/decisions/0021) : s'il s'écarte de plus que la tolérance, la session reste ouverte et le premier
    /// comptage est gardé ; le recomptage est définitif, quel que soit l'écart.
    /// </summary>
    public CountOutcome Count(Money counted, DateTimeOffset at, Money tolerance, bool isRecount, string? detail = null)
    {
        EnsureOpen();
        if (!isRecount && Math.Abs(counted.Amount - ExpectedCash.Amount) > tolerance.Amount)
        {
            FirstCount = counted;
            FirstCountDetail = detail;
            return CountOutcome.NeedsRecount;
        }

        Close(counted, at);
        CountDetail = detail;
        return CountOutcome.Closed;
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
        long? cashVariance,
        Money cashRefunded = default,
        Money cashPaidOut = default,
        Money cashPaidIn = default,
        Money? firstCount = null,
        string? firstCountDetail = null,
        string? countDetail = null) =>
        new(id, assignment, openingFloat, openedAt)
        {
            CashRefunded = cashRefunded,
            CashPaidOut = cashPaidOut,
            CashPaidIn = cashPaidIn,
            FirstCount = firstCount,
            FirstCountDetail = firstCountDetail,
            CountDetail = countDetail,
            Status = status,
            CashReceived = cashReceived,
            CountedCash = countedCash,
            ClosedAt = closedAt,
            CashVariance = cashVariance,
        };

    private void EnsureOpen() =>
        Ensure.That(Status == SessionStatus.Open, "La session de caisse est clôturée (RG-01).");
}
