using Soro.Domain.Identity;

namespace Soro.Application.Identity;

/// <summary>Droits par profil (cahier des charges, section 3).</summary>
public static class RolePolicy
{
    public static bool IsManager(Role role) => role is Role.Owner or Role.Manager;

    /// <summary>Consultation des ventes, des écarts de caisse et des rapports : le comptable lit sans modifier.</summary>
    public static bool CanViewReports(Role role) => role is Role.Owner or Role.Manager or Role.Accountant;

    /// <summary>Journal des actions sensibles : propriétaire et comptable (surveillance et lecture), pas les profils qu'il surveille.</summary>
    public static bool CanReadAuditLog(Role role) => role is Role.Owner or Role.Accountant;

    /// <summary>Annuaire (identifiant et nom) pour nommer les auteurs : profils du stock et comptable.</summary>
    public static bool CanReadDirectory(Role role) => CanManageStock(role) || role == Role.Accountant;

    /// <summary>Lecture de l'équipe (identifiants, rôles, états) : propriétaire et gérant, ceux qui la gèrent.</summary>
    public static bool CanListTeam(Role role) => IsManager(role);

    /// <summary>Profils qu'un utilisateur peut créer ou désactiver, dans l'ordre des rôles.</summary>
    public static IReadOnlyList<Role> AssignableRoles(Role actor) =>
        Enum.GetValues<Role>().Where(target => CanManageRole(actor, target)).ToArray();

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
