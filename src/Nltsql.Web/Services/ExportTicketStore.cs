using Microsoft.Extensions.Caching.Memory;
using Nltsql.Core.Queries;

namespace Nltsql.Web.Services;

/// <summary>
/// Hands out short-lived tickets that let a browser download a CSV.
/// </summary>
/// <remarks>
/// A download has to be a plain GET the browser performs itself, but the
/// query behind it must not travel in the URL — it would be logged, and
/// a hand-edited URL would become a way to ask for data the caller is
/// not entitled to. Instead the interactive circuit registers the query
/// it just ran and the browser fetches an opaque ticket. Tickets are
/// bound to the tenant and expire in minutes.
/// </remarks>
public sealed class ExportTicketStore(IMemoryCache cache, TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Issue(string tenantId, SemanticQuery query, string title)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ticket = Guid.NewGuid().ToString("N");

        cache.Set(
            Key(ticket),
            new ExportTicket(tenantId, query, title, timeProvider.GetUtcNow()),
            Lifetime);

        return ticket;
    }

    /// <summary>Returns the ticket only if it exists and belongs to this tenant.</summary>
    public ExportTicket? Redeem(string ticket, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(ticket) || !cache.TryGetValue(Key(ticket), out ExportTicket? entry))
        {
            return null;
        }

        return entry is not null && entry.TenantId == tenantId ? entry : null;
    }

    private static string Key(string ticket) => $"export-ticket:{ticket}";
}

public sealed record ExportTicket(string TenantId, SemanticQuery Query, string Title, DateTimeOffset IssuedAt);
