using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Domain.Sales;

public enum ReturnKind
{
    /// <summary>Une partie de la vente est rendue.</summary>
    Return,

    /// <summary>Toute la vente est annulée : tout ce qui restait est rendu.</summary>
    Cancellation,
}

/// <summary>Ce que le client rapporte : une ligne de la vente (sa position), une quantité, et où va la marchandise.</summary>
/// <param name="LineIndex">Position de la ligne dans la vente (0 pour la première).</param>
/// <param name="Quantity">Pièces, ou millièmes (grammes, millilitres) pour un produit pesé.</param>
/// <param name="Resellable"><c>true</c> : retour en rayon ; <c>false</c> : marchandise abîmée, qui part en perte.</param>
public sealed record ReturnRequest(int LineIndex, int Quantity, bool Resellable);

public sealed record ReturnedLine(int LineIndex, int Quantity, bool Resellable);

/// <summary>Une part du remboursement, par moyen de paiement d'origine.</summary>
public sealed record RefundPart(PaymentMethod Method, Money Amount);

/// <summary>Le calcul d'un retour, avant qu'il soit enregistré : quelles lignes, combien rendre et par quel moyen.</summary>
public sealed record ReturnPlan(IReadOnlyList<ReturnedLine> Lines, IReadOnlyList<RefundPart> Refund)
{
    public Money Total => Money.Sum(Refund.Select(part => part.Amount));

    /// <summary>La part à sortir du tiroir.</summary>
    public Money Cash => Money.Sum(Refund.Where(part => part.Method == PaymentMethod.Cash).Select(part => part.Amount));

    /// <summary>La part rendue en effaçant une dette du client.</summary>
    public Money Credit => Money.Sum(Refund.Where(part => part.Method == PaymentMethod.Credit).Select(part => part.Amount));
}

/// <summary>
/// Opération inverse d'une vente (RG-02) : jamais de suppression, la vente d'origine reste lisible. Elle garde qui l'a faite, qui l'a validée, pourquoi,
/// quoi et combien (docs/decisions/0020).
/// </summary>
public sealed record SaleReturn
{
    private SaleReturn(
        SaleReturnId id,
        TenantId tenant,
        SaleId saleId,
        ReturnKind kind,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy,
        string reason,
        IReadOnlyList<ReturnedLine> lines,
        IReadOnlyList<RefundPart> refund,
        SessionId? refundSession)
    {
        Id = id;
        Tenant = tenant;
        SaleId = saleId;
        Kind = kind;
        At = at;
        By = by;
        ApprovedBy = approvedBy;
        Reason = reason;
        Lines = lines;
        Refund = refund;
        RefundSession = refundSession;
    }

    public SaleReturnId Id { get; }

    public TenantId Tenant { get; }

    public SaleId SaleId { get; }

    public ReturnKind Kind { get; }

    public DateTimeOffset At { get; }

    public UserId By { get; }

    /// <summary>Le gérant qui a validé avec son PIN ; <c>null</c> quand l'auteur est lui-même gérant ou propriétaire.</summary>
    public UserId? ApprovedBy { get; }

    public string Reason { get; }

    public IReadOnlyList<ReturnedLine> Lines { get; }

    public IReadOnlyList<RefundPart> Refund { get; }

    /// <summary>La session de caisse dont le tiroir a rendu les espèces ; <c>null</c> quand rien n'est sorti du tiroir.</summary>
    public SessionId? RefundSession { get; }

    public Money Total => Money.Sum(Refund.Select(part => part.Amount));

    public Money Cash => Money.Sum(Refund.Where(part => part.Method == PaymentMethod.Cash).Select(part => part.Amount));

    public static SaleReturn Record(
        SaleReturnId id,
        Sale sale,
        ReturnKind kind,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy,
        string reason,
        ReturnPlan plan,
        SessionId? refundSession)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(reason), "Le motif est obligatoire.");
        Ensure.That(reason.Trim().Length <= 500, "Le motif ne peut pas dépasser 500 caractères.");
        Ensure.That(at >= sale.RecordedAt, "Un retour ne peut pas précéder la vente.");
        Ensure.That(plan.Cash == Money.Zero || refundSession is not null, "Ouvrez la caisse pour rembourser en espèces.");
        return new SaleReturn(id, sale.Assignment.Tenant, sale.Id, kind, at, by, approvedBy, reason.Trim(), plan.Lines, plan.Refund, refundSession);
    }

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées.</summary>
    public static SaleReturn Restore(
        SaleReturnId id,
        TenantId tenant,
        SaleId saleId,
        ReturnKind kind,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy,
        string reason,
        IReadOnlyList<ReturnedLine> lines,
        IReadOnlyList<RefundPart> refund,
        SessionId? refundSession) =>
        new(id, tenant, saleId, kind, at, by, approvedBy, reason, lines, refund, refundSession);
}
