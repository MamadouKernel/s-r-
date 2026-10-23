using Soro.Domain.Common;

namespace Soro.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Valide d'un seul bloc tout ce qui a été écrit pendant le cas d'usage.</summary>
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
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
    public const string CancelRefused = "sale.cancel.refused";
    public const string UserDisabled = "user.disabled";
    public const string ProductCreated = "product.created";
    public const string ProductUpdated = "product.updated";
    public const string ProductDeactivated = "product.deactivated";
    public const string StockLoss = "stock.loss";
    public const string CommerceCreated = "commerce.created";
    public const string PasswordReset = "user.password.reset";
}
