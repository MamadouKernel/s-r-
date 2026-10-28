namespace Soro.Domain.Common;

// Identifiants typés : empêchent de confondre deux UUID et sont générables
// hors ligne (RG-17). Version 7 : triable dans le temps, favorable aux index.

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());
}

public readonly record struct PointOfSaleId(Guid Value)
{
    public static PointOfSaleId New() => new(Guid.CreateVersion7());
}

public readonly record struct RegisterId(Guid Value)
{
    public static RegisterId New() => new(Guid.CreateVersion7());
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
}

public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.CreateVersion7());
}

public readonly record struct DeviceInvitationId(Guid Value)
{
    public static DeviceInvitationId New() => new(Guid.CreateVersion7());
}

public readonly record struct DebtRepaymentId(Guid Value)
{
    public static DebtRepaymentId New() => new(Guid.CreateVersion7());
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
}

public readonly record struct CashMovementId(Guid Value)
{
    public static CashMovementId New() => new(Guid.CreateVersion7());
}

public readonly record struct SaleReturnId(Guid Value)
{
    public static SaleReturnId New() => new(Guid.CreateVersion7());
}

public readonly record struct SaleId(Guid Value)
{
    public static SaleId New() => new(Guid.CreateVersion7());
}

public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());
}

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());
}

public readonly record struct FamilyId(Guid Value)
{
    public static FamilyId New() => new(Guid.CreateVersion7());
}

public readonly record struct PhotoId(Guid Value)
{
    public static PhotoId New() => new(Guid.CreateVersion7());
}

public readonly record struct SubscriptionPaymentId(Guid Value)
{
    public static SubscriptionPaymentId New() => new(Guid.CreateVersion7());
}

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());
}

public readonly record struct StockMovementId(Guid Value)
{
    public static StockMovementId New() => new(Guid.CreateVersion7());
}
