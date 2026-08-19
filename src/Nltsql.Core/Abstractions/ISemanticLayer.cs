using Nltsql.Core.Queries;
using Nltsql.Core.Results;
using Nltsql.Core.Semantics;

namespace Nltsql.Core.Abstractions;

/// <summary>The only way the application reaches data.</summary>
/// <remarks>
/// There is deliberately no method that accepts SQL. Everything the app
/// can ask for has to be expressible as a <see cref="SemanticQuery"/>,
/// which keeps metric definitions in one place and keeps the warehouse
/// out of reach of the UI.
/// </remarks>
public interface ISemanticLayer
{
    /// <summary>Views, measures and dimensions the caller may query.</summary>
    Task<SemanticModel> GetModelAsync(CancellationToken cancellationToken = default);

    Task<QueryResultSet> ExecuteAsync(SemanticQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Identifies the caller and the data they may see.</summary>
/// <remarks>
/// Populated per request and projected into the Cube JWT, where
/// <c>queryRewrite</c> turns it into a mandatory filter. The prototype
/// resolves it from configuration; a real deployment resolves it from
/// the authenticated principal.
/// </remarks>
public interface ITenantContext
{
    string TenantId { get; }

    string UserName { get; }

    /// <summary>Sites the caller may see; empty means unrestricted.</summary>
    IReadOnlyList<string> AllowedSites { get; }
}
