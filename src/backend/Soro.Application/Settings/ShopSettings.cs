using Soro.Domain.Common;

namespace Soro.Application.Settings;

/// <summary>Paramètres du commerce modifiables par le propriétaire (règles marquées « paramétrable »).</summary>
/// <param name="CashVarianceToleranceFcfa">Écart de clôture toléré sans recomptage ni signalement (valeur de départ, à régler avec le pilote : docs/decisions/0021).</param>
/// <param name="CashOutApprovalThresholdFcfa">Au-delà, une dépense ou un versement demande l'accord d'un gérant.</param>
/// <param name="OpenSessionAlertHours">Une caisse ouverte plus longtemps est signalée.</param>
/// <param name="DrawerWarningFcfa">Au-delà, la caisse avertit « faites un versement au coffre ».</param>
public sealed record ShopSettings(
    int DiscountApprovalThresholdPercent,
    long DefaultCreditCeilingFcfa,
    long CashVarianceToleranceFcfa = 500,
    long CashOutApprovalThresholdFcfa = 5_000,
    int OpenSessionAlertHours = 14,
    long DrawerWarningFcfa = 100_000)
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
