using Soro.Application.Identity;
using Soro.Domain.Cash;

namespace Soro.Application.Cash;

internal static class SessionAccess
{
    /// <summary>Le caissier titulaire agit sur sa session ; un gérant ou le propriétaire sur toutes.</summary>
    public static bool CanAct(ICurrentUser user, CashRegisterSession session) =>
        RolePolicy.CanOperateRegister(user.Role)
        && (session.Assignment.Cashier == user.User || RolePolicy.IsManager(user.Role));
}
