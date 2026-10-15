using Soro.Domain.Common;

namespace Soro.Application.Settings;

/// <summary>Paramètres du commerce modifiables par le propriétaire (règles marquées « paramétrable »).</summary>
public sealed record ShopSettings(int DiscountApprovalThresholdPercent)
{
    /// <summary>Seuil par défaut de RG-03 : au-delà de 10 %, la remise exige un gérant.</summary>
    public static ShopSettings Default { get; } = new(10);
}

public interface IShopSettings
{
    Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken);
}
