using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Infrastructure.Persistence;

/// <summary>
/// The only sanctioned way to read tenant-owned rows across organizations. It is restricted to
/// platform administrators (or system scopes such as bootstrap/migration) and must be paired with
/// field-level restrictions in the calling service (specification §4.3).
/// </summary>
public static class PlatformScopeExtensions
{
    public static IQueryable<TEntity> AsPlatformScope<TEntity>(this IQueryable<TEntity> query, ITenantContext tenantContext)
        where TEntity : class, ITenantOwned
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (!tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Platform scope requires a platform administrator principal.");
        }

        return query.IgnoreQueryFilters();
    }
}
