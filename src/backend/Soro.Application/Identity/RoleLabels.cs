using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Nom des profils tel que l'équipe les lit (journal, messages).</summary>
public static class RoleLabels
{
    public static string French(Role role) => role switch
    {
        Role.Owner => "Propriétaire",
        Role.Manager => "Gérant",
        Role.Cashier => "Caissier",
        Role.StockKeeper => "Magasinier",
        Role.Accountant => "Comptable",
        _ => role.ToString(),
    };
}
