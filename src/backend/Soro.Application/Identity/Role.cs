using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Droits par profil (cahier des charges, section 3).</summary>
public static class RolePolicy
{
    public static bool IsManager(Role role) => role is Role.Owner or Role.Manager;

    /// <summary>Fiches produit et prix : gérant et propriétaire (cahier des charges, section 3).</summary>
    public static bool CanManageCatalogue(Role role) => IsManager(role);

    /// <summary>Réceptions, pertes et inventaires. Le prix d'achat leur est aussi visible.</summary>
    public static bool CanManageStock(Role role) => role is Role.Owner or Role.Manager or Role.StockKeeper;

    public static bool CanSeePurchasePrice(Role role) => CanManageStock(role);

    public static bool CanOperateRegister(Role role) => role is Role.Owner or Role.Manager or Role.Cashier;

    /// <summary>
    /// Qui crée ou désactive quel profil : le propriétaire gère tous les profils sauf un autre propriétaire,
    /// un gérant gère les caissiers et les magasiniers, les autres profils ne gèrent personne.
    /// </summary>
    public static bool CanManageRole(Role actor, Role target) => actor switch
    {
        Role.Owner => target != Role.Owner,
        Role.Manager => target is Role.Cashier or Role.StockKeeper,
        _ => false,
    };
}
