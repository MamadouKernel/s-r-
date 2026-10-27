using Soro.Domain.Common;

namespace Soro.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Valide d'un seul bloc tout ce qui a été écrit pendant le cas d'usage.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConflictException">Une écriture concurrente ou un doublon a empêché l'enregistrement : rien n'a été écrit.</exception>
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Un doublon (clé unique) ou une écriture concurrente a fait échouer l'enregistrement ; la transaction entière est annulée.</summary>
public sealed class ConflictException(Exception inner) : Exception("Opération concurrente ou doublon détecté.", inner);

public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>Une inscription de cette action sur cette ressource existe déjà (pour qu'un envoi rejoué ne l'écrive pas deux fois).</summary>
    Task<bool> ExistsAsync(string action, Guid resourceId, CancellationToken cancellationToken);
}

/// <summary>Action sensible tracée : qui, quoi, sur quelle ressource, quand (journal des actions sensibles).</summary>
public sealed record AuditEntry(
    TenantId Tenant,
    UserId User,
    string Action,
    Guid ResourceId,
    string Detail,
    DateTimeOffset At);

public static class AuditActions
{
    public const string DiscountApproved = "sale.discount.approved";
    public const string DiscountRefused = "sale.discount.refused";
    public const string SaleCancelled = "sale.cancelled";
    public const string SaleReturned = "sale.returned";
    public const string ReturnRefused = "sale.return.refused";
    public const string CancelRefused = "sale.cancel.refused";
    public const string SalePriceLowered = "sale.price.lowered";
    public const string UserDisabled = "user.disabled";
    public const string UserPinReset = "user.pin.reset";
    public const string UserEnabled = "user.enabled";
    public const string UserRoleChanged = "user.role.changed";
    public const string ProductCreated = "product.created";
    public const string ProductUpdated = "product.updated";
    public const string ProductDeactivated = "product.deactivated";
    public const string ProductBarcodeCreated = "product.barcode.created";
    public const string FamilyCreated = "family.created";
    public const string ProductsGrouped = "products.grouped";
    public const string ProductsImported = "products.imported";
    public const string SubscriptionPaymentDeclared = "subscription.payment.declared";
    public const string SubscriptionPaymentValidated = "subscription.payment.validated";
    public const string SubscriptionPaymentRefused = "subscription.payment.refused";
    public const string SubscriptionExpiredSale = "subscription.expired.sale";
    public const string DataExported = "data.exported";
    public const string StockLoss = "stock.loss";
    public const string CommerceCreated = "commerce.created";
    public const string PasswordReset = "user.password.reset";
    public const string DebtRepaid = "debt.repaid";
    public const string CreditApproved = "credit.limit.approved";
    public const string CreditRefused = "credit.limit.refused";
    public const string CreditExceeded = "credit.limit.exceeded";
    public const string DeviceInvited = "device.invited";
    public const string DeviceEnrolled = "device.enrolled";
    public const string DeviceRevoked = "device.revoked";
    public const string DeviceInvitationRevoked = "device.invitation.revoked";
    public const string LoginOffline = "login.offline";
}
