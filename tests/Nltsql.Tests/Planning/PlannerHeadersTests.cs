using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

public sealed class PlannerHeadersTests
{
    private static HttpRequestMessage Apply(PlannerOptions options)
    {
        var request = new HttpRequestMessage();
        PlannerHeaders.Apply(request.Headers, options);

        return request;
    }

    private static string? Value(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public void Sends_the_api_key_and_the_user_token()
    {
        using var request = Apply(new PlannerOptions { ApiKey = "geheim", UserToken = "benutzer-1" });

        Value(request, "X-Api-Key").ShouldBe("geheim");
        Value(request, "X-User-Token").ShouldBe("benutzer-1");
    }

    [Fact]
    public void Uses_the_configured_header_names()
    {
        using var request = Apply(new PlannerOptions
        {
            ApiKey = "Bearer abc",
            ApiKeyHeader = "Authorization",
            UserToken = "u1",
            UserTokenHeader = "X-Tenant-User",
        });

        // Gateways disagree about header names; a bearer scheme is just
        // a different name and a prefixed value.
        Value(request, "Authorization").ShouldBe("Bearer abc");
        Value(request, "X-Tenant-User").ShouldBe("u1");
    }

    [Fact]
    public void Omits_a_credential_that_is_not_configured()
    {
        using var request = Apply(new PlannerOptions { ApiKey = "nur-key", UserToken = "   " });

        Value(request, "X-Api-Key").ShouldBe("nur-key");

        // A present-but-empty header can read as a failed authentication
        // attempt to a gateway, which is worse than sending nothing.
        Value(request, "X-User-Token").ShouldBeNull();
    }

    [Fact]
    public void Sends_nothing_when_no_credentials_are_configured()
    {
        using var request = Apply(new PlannerOptions());

        request.Headers.ShouldBeEmpty();
    }

    [Fact]
    public void Adds_further_fixed_headers()
    {
        using var request = Apply(new PlannerOptions
        {
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Request-Source"] = "nltsql",
            },
        });

        Value(request, "X-Request-Source").ShouldBe("nltsql");
    }

    [Fact]
    public void Ignores_a_malformed_header_name_instead_of_throwing()
    {
        // Thrown from inside the DI container this would surface at first
        // use with a message that says nothing about configuration.
        using var request = Apply(new PlannerOptions { ApiKey = "x", ApiKeyHeader = "  " });

        request.Headers.ShouldBeEmpty();
    }
}
