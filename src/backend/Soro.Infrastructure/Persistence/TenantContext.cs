using Soro.Application.Identity;
using Soro.Domain.Common;

namespace Soro.Infrastructure.Persistence;

/// <summary>Source du commerce courant pour l'isolation par commerce de la base (<c>app.tenant_id</c>).</summary>
public interface ITenantAccessor
{
    /// <summary>Le commerce de la requête, ou <c>null</c> s'il n'est pas encore connu (la base ne montre alors rien).</summary>
    TenantId? Current { get; }
}

/// <summary>
/// Commerce de la requête en cours, posé une fois : par l'authentification pour les requêtes avec jeton, par la
/// connexion pour celles qui n'en ont pas encore. Tant qu'il n'est pas posé, la base n'expose aucune ligne.
/// </summary>
public sealed class TenantContext : ITenantAccessor, ITenantScope
{
    public TenantId? Current { get; private set; }

    public void Enter(TenantId tenant) => Current = tenant;
}

public sealed class CurrentUserTenantAccessor(ICurrentUser user) : ITenantAccessor
{
    public TenantId? Current => user.Tenant;
}
