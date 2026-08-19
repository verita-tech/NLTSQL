using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nltsql.Core.Abstractions;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Security;

/// <summary>
/// Mints the short-lived JWT that carries the caller's security context
/// to Cube.
/// </summary>
/// <remarks>
/// The token is created server-side per request and never leaves the
/// server. Cube verifies it against the shared secret and applies
/// <c>queryRewrite</c>, so the tenant filter is enforced by the semantic
/// layer rather than trusted from the caller.
/// </remarks>
public sealed class CubeTokenFactory(IOptions<CubeOptions> options, TimeProvider timeProvider)
{
    private readonly CubeOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public string Create(ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.ApiSecret));

        var claims = new Dictionary<string, object>
        {
            ["tenant_id"] = tenant.TenantId,
            ["user"] = tenant.UserName,
        };

        // Absent claim means unrestricted; cube.js treats an empty list
        // the same way, but leaving it out keeps admin tokens obvious.
        if (tenant.AllowedSites.Count > 0)
        {
            claims["allowed_sites"] = tenant.AllowedSites.ToArray();
        }

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_options.TokenLifetime),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }
}
