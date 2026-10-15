namespace Soro.Domain.Identity;

/// <summary>Profils du cahier des charges (section 3). Les droits associés sont dans la couche Application.</summary>
public enum Role
{
    Owner,
    Manager,
    Cashier,
    StockKeeper,
    Accountant,
}
