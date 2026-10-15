namespace Soro.Application.Identity;

public enum Role
{
    Owner,
    Manager,
    Cashier,
    StockKeeper,
    Accountant,
}

/// <summary>Droits par profil (cahier des charges, section 3).</summary>
public static class RolePolicy
{
    public static bool IsManager(Role role) => role is Role.Owner or Role.Manager;

    public static bool CanOperateRegister(Role role) => role is Role.Owner or Role.Manager or Role.Cashier;
}
