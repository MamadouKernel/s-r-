using Soro.Domain.Common;
using Soro.Domain.Subscription;
using Soro.Domain.Cash;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;
using Soro.Domain.Tenancy;

namespace Soro.Infrastructure.Persistence;

// Modèles de persistance : ce que contiennent les tables. Ils ne sont pas les entités du domaine
// (docs/architecture.md) et ne sortent jamais de l'infrastructure ; les mappeurs font la traduction.

internal sealed class SessionRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PointOfSaleId { get; set; }

    public Guid RegisterId { get; set; }

    public Guid CashierId { get; set; }

    public long OpeningFloat { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public SessionStatus Status { get; set; }

    public long CashReceived { get; set; }

    /// <summary>Espèces rendues aux clients pendant la session (annulations et retours).</summary>
    public long CashRefunded { get; set; }

    public long? CountedCash { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public long? CashVariance { get; set; }
}

internal sealed class SaleRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SessionId { get; set; }

    public Guid PointOfSaleId { get; set; }

    public Guid RegisterId { get; set; }

    public Guid CashierId { get; set; }

    public long LocalSequence { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public long Discount { get; set; }

    public Guid? CustomerId { get; set; }

    public long GrossTotal { get; set; }

    public long NetTotal { get; set; }

    public SaleStatus Status { get; set; }

    public Guid? CancelledBy { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public List<SaleLineRow> Lines { get; } = [];

    public List<PaymentRow> Payments { get; } = [];
}

internal sealed class SaleLineRow
{
    public Guid SaleId { get; set; }

    public int Position { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    /// <summary>Unité de la ligne au moment de la vente (copie figée).</summary>
    public MeasureUnit Unit { get; set; }

    public long UnitPrice { get; set; }

    /// <summary>Prix d'achat moyen retenu à la vente (RG-11) ; <c>null</c> pour un produit inconnu ou une vente antérieure.</summary>
    public long? UnitCost { get; set; }
}

internal sealed class PaymentRow
{
    public Guid Id { get; set; }

    public Guid SaleId { get; set; }

    public int Position { get; set; }

    public Guid TenantId { get; set; }

    public PaymentMethod Method { get; set; }

    public long Amount { get; set; }

    public long Change { get; set; }

    public string? Reference { get; set; }

    /// <summary>Référence normalisée (majuscules) : porte l'unicité insensible à la casse (RG-04).</summary>
    public string? ReferenceKey { get; set; }
}

internal sealed class UserRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Identifier { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public Role Role { get; set; }

    public string? PinHash { get; set; }

    /// <summary>Numéro canonique du propriétaire ; <c>null</c> pour les profils qui se connectent par PIN.</summary>
    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public UserStatus Status { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Les sessions ouvertes avant cet instant ne sont plus valables (nouveau PIN, désactivation, changement de rôle).</summary>
    public DateTimeOffset? SessionsValidFrom { get; set; }
}

internal sealed class AuditRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid ResourceId { get; set; }

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}

internal sealed class ProductRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string? Category { get; set; }

    public long PurchasePrice { get; set; }

    /// <summary>Unité de vente (docs/decisions/0017) ; les quantités d'un produit pesé sont en millièmes.</summary>
    public MeasureUnit Unit { get; set; }

    public long SalePrice { get; set; }

    public int AlertThreshold { get; set; }

    public bool TracksStock { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public int QuantityOnHand { get; set; }

    public Guid FamilyId { get; set; }

    public string? FormatLabel { get; set; }

    public Guid? PhotoId { get; set; }
}

internal sealed class ProductFamilyRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? PhotoId { get; set; }
}

internal sealed class PhotoRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Photo retirée : le rôle applicatif ne supprime jamais de ligne, l'image est donc vidée et la ligne marquée.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}

internal sealed class StockMovementRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProductId { get; set; }

    public StockMovementKind Kind { get; set; }

    public int QuantityDelta { get; set; }

    public string? Reason { get; set; }

    public Guid? SourceId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>Prix d'achat unitaire de la livraison, pour une réception qui l'a indiqué.</summary>
    public long? UnitCost { get; set; }
}

internal sealed class CommerceRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ActivityType Activity { get; set; }

    public string OwnerEmail { get; set; } = string.Empty;

    public string Formula { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset TrialEndsAt { get; set; }

    public string TermsVersion { get; set; } = string.Empty;

    public DateTimeOffset TermsAcceptedAt { get; set; }

    public DateTimeOffset? PaidUntil { get; set; }
}

/// <summary>Numéro → commerce et utilisateur. Hors isolation par commerce : c'est ce qui permet de trouver le commerce avant de le connaître.</summary>
internal sealed class OwnerDirectoryRow
{
    public string Email { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }
}

internal sealed class EmailVerificationRow
{
    public Guid Id { get; set; }

    public VerificationPurpose Purpose { get; set; }

    public string Email { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public string? Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int Attempts { get; set; }

    public int ResendCount { get; set; }

    public DateTimeOffset LastSentAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}

internal sealed class DeviceRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    public Guid InvitationId { get; set; }

    public DateTimeOffset EnrolledAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }
}

internal sealed class DeviceInvitationRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string SecretHash { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }

    public string? SuggestedName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class CustomerRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Numéro canonique <c>+225XXXXXXXXXX</c> ; <c>null</c> si le client n'en a pas donné.</summary>
    public string? Phone { get; set; }

    public long CreditCeiling { get; set; }

    public long Debt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive { get; set; }
}

internal sealed class DebtRepaymentRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }

    public long Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public string? Reference { get; set; }

    /// <summary>Référence normalisée (majuscules) : porte l'unicité insensible à la casse, comme pour les ventes.</summary>
    public string? ReferenceKey { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset At { get; set; }
}

internal sealed class SubscriptionPaymentRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public SubscriptionPeriod Period { get; set; }

    public SubscriptionMethod Method { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string ReferenceKey { get; set; } = string.Empty;

    public long Amount { get; set; }

    public DateTimeOffset DeclaredAt { get; set; }

    public SubscriptionPaymentStatus Status { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset? CoveredFrom { get; set; }

    public DateTimeOffset? CoveredTo { get; set; }
}

/// <summary>Références de paiement déjà utilisées. Hors isolation par commerce, comme l'annuaire des propriétaires : c'est ce qui permet de refuser une référence déjà vue chez un autre commerce.</summary>
internal sealed class SubscriptionReferenceRow
{
    public SubscriptionMethod Method { get; set; }

    public string ReferenceKey { get; set; } = string.Empty;
}

internal sealed class SubscriptionReminderRow
{
    public Guid TenantId { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}

internal sealed class SaleReturnRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SaleId { get; set; }

    /// <summary>La session dont le tiroir a rendu les espèces ; <c>null</c> quand rien n'est sorti du tiroir.</summary>
    public Guid? RefundSessionId { get; set; }

    public ReturnKind Kind { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid ByUserId { get; set; }

    public Guid? ApprovedBy { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long Total { get; set; }

    public List<SaleReturnLineRow> Lines { get; set; } = [];

    public List<SaleReturnRefundRow> Refunds { get; set; } = [];
}

internal sealed class SaleReturnLineRow
{
    public Guid ReturnId { get; set; }

    public int Position { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Position de la ligne dans la vente d'origine.</summary>
    public int LineIndex { get; set; }

    public int Quantity { get; set; }

    public bool Resellable { get; set; }
}

internal sealed class SaleReturnRefundRow
{
    public Guid ReturnId { get; set; }

    public int Position { get; set; }

    public Guid TenantId { get; set; }

    public PaymentMethod Method { get; set; }

    public long Amount { get; set; }
}
