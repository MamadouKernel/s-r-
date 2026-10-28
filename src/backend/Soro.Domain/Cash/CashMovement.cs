using Soro.Domain.Common;

namespace Soro.Domain.Cash;

public enum CashMovementKind
{
    /// <summary>Une dépense payée du tiroir (transport, sachets, repas…).</summary>
    Expense,

    /// <summary>De l'argent retiré du tiroir pour le coffre ou le patron : à ne pas confondre avec une dépense.</summary>
    Drop,

    /// <summary>De l'argent ajouté au tiroir (monnaie).</summary>
    Topup,
}

/// <summary>
/// Une sortie ou une entrée d'espèces en cours de journée (docs/decisions/0021). Jamais modifiée ni supprimée : une erreur se corrige par un
/// mouvement inverse motivé.
/// </summary>
public sealed record CashMovement
{
    public const int MaxReasonLength = 200;

    private CashMovement(
        CashMovementId id,
        TenantId tenant,
        SessionId session,
        CashMovementKind kind,
        Money amount,
        string? reason,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy)
    {
        Id = id;
        Tenant = tenant;
        Session = session;
        Kind = kind;
        Amount = amount;
        Reason = reason;
        At = at;
        By = by;
        ApprovedBy = approvedBy;
    }

    public CashMovementId Id { get; }

    public TenantId Tenant { get; }

    public SessionId Session { get; }

    public CashMovementKind Kind { get; }

    public Money Amount { get; }

    public string? Reason { get; }

    public DateTimeOffset At { get; }

    public UserId By { get; }

    /// <summary>Le gérant qui a validé une grosse sortie ; <c>null</c> quand elle n'en demandait pas ou que l'auteur est gérant.</summary>
    public UserId? ApprovedBy { get; }

    public static CashMovement Record(
        CashMovementId id,
        TenantId tenant,
        SessionId session,
        CashMovementKind kind,
        Money amount,
        string? reason,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy)
    {
        Ensure.That(amount > Money.Zero, "Le montant doit être supérieur à zéro.");
        var clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Ensure.That(kind != CashMovementKind.Expense || clean is not null, "Dites à quoi sert cette dépense.");
        Ensure.That(clean is null || clean.Length <= MaxReasonLength, $"Le motif ne peut pas dépasser {MaxReasonLength} caractères.");
        return new CashMovement(id, tenant, session, kind, amount, clean, at, by, approvedBy);
    }

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées.</summary>
    public static CashMovement Restore(
        CashMovementId id,
        TenantId tenant,
        SessionId session,
        CashMovementKind kind,
        Money amount,
        string? reason,
        DateTimeOffset at,
        UserId by,
        UserId? approvedBy) =>
        new(id, tenant, session, kind, amount, reason, at, by, approvedBy);
}
