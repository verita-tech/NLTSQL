using Microsoft.Extensions.Options;
using Nltsql.Core.Abstractions;

namespace Nltsql.Web.Services;

/// <summary>
/// Resolves the caller's tenant and data scope.
/// </summary>
/// <remarks>
/// The prototype reads this from configuration so the stack runs without
/// an identity provider. The seam is what matters: everything downstream
/// — the Cube JWT, the row filter Cube enforces, and every store query —
/// already goes through <see cref="ITenantContext"/>, so adding real
/// authentication means replacing this one class with one that reads the
/// authenticated principal, not touching the rest of the app.
/// </remarks>
public sealed class ConfiguredTenantContext(IOptions<TenantOptions> options) : ITenantContext
{
    private readonly TenantOptions _options = options.Value;

    public string TenantId => _options.TenantId;

    public string UserName => _options.UserName;

    public IReadOnlyList<string> AllowedSites => _options.AllowedSites;
}

public sealed class TenantOptions
{
    public const string SectionName = "Tenant";

    public string TenantId { get; set; } = "demo";

    public string UserName { get; set; } = "Demo-Benutzer";

    /// <summary>Sites this tenant may see. Empty means unrestricted.</summary>
    public IReadOnlyList<string> AllowedSites { get; set; } = [];
}
