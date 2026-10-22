using Soro.Domain.Cash;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

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

    public long UnitPrice { get; set; }
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

    public string PinHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public UserStatus Status { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
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

    public long SalePrice { get; set; }

    public int AlertThreshold { get; set; }

    public bool TracksStock { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public int QuantityOnHand { get; set; }
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
}
