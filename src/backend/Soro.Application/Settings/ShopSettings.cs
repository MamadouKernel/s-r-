using Soro.Domain.Common;

namespace Soro.Application.Settings;

/// <summary>Paramètres du commerce modifiables par le propriétaire (règles marquées « paramétrable »).</summary>
public sealed record ShopSettings(int DiscountApprovalThresholdPercent, long DefaultCreditCeilingFcfa)
{
    /// <summary>Plafond de crédit d'un nouveau client tant que les paramètres du commerce ne sont pas stockés (T028) : provisoire, à valider avec le pilote.</summary>
    public const long DefaultCreditCeiling = 20_000;

    /// <summary>Seuil par défaut de RG-03 : au-delà de 10 %, la remise exige un gérant.</summary>
    public static ShopSettings Default { get; } = new(10, DefaultCreditCeiling);
}

public interface IShopSettings
{
    Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken);
}
