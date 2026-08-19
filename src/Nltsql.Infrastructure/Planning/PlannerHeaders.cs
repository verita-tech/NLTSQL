using System.Net.Http.Headers;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Applies the fixed credential headers for the Ollama client.
/// </summary>
/// <remarks>
/// Separated from the DI wiring so the header policy can be tested on
/// its own — which headers appear, which are omitted, and what happens
/// with a malformed configured name.
/// </remarks>
public static class PlannerHeaders
{
    public static void Apply(HttpRequestHeaders headers, PlannerOptions options)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(options);

        Add(headers, options.ApiKeyHeader, options.ApiKey);
        Add(headers, options.UserTokenHeader, options.UserToken);

        foreach (var (name, value) in options.DefaultHeaders)
        {
            Add(headers, name, value);
        }
    }

    private static void Add(HttpRequestHeaders headers, string? name, string? value)
    {
        // A blank credential is not the same as an absent one: some
        // gateways treat a present-but-empty header as an attempt to
        // authenticate and reject the request outright.
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // TryAdd rather than Add: a misconfigured header name would
        // otherwise throw from inside the DI container at first use,
        // where the message says nothing about configuration.
        headers.TryAddWithoutValidation(name.Trim(), value);
    }
}
