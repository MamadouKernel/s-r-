using Soro.Domain.Common;

namespace Soro.Domain.Cash;

/// <summary>Contexte d'attribution d'une session : commerce, point de vente, caisse et caissier (REQ-07).</summary>
public sealed record RegisterAssignment(
    TenantId Tenant,
    PointOfSaleId PointOfSale,
    RegisterId Register,
    UserId Cashier);
