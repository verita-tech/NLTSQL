using Microsoft.EntityFrameworkCore;
using NLTSQL.Data;
using NLTSQL.Web.Data;

namespace NLTSQL.Web;

/// <summary>Creates a short-lived application-store context.</summary>
/// <remarks>
/// Wraps EF Core's own pooled factory. The indirection exists so the data project never has to
/// know about <see cref="ApplicationDbContext"/>, which also carries the Identity schema.
/// </remarks>
public sealed class NltsqlDataContextFactory(IDbContextFactory<ApplicationDbContext> factory) : INltsqlDataContextFactory
{
    /// <inheritdoc/>
    public INltsqlDataContext CreateContext() => factory.CreateDbContext();
}
