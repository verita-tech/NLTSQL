using System.Text;
using NLTSQL.Core.Query;

namespace NLTSQL.QueryEngine.Sql;

/// <summary>
/// The parts of SQL generation that genuinely differ between Oracle and PostgreSQL.
/// </summary>
/// <remarks>
/// Kept deliberately small. Everything the two engines spell identically — aggregates, arithmetic,
/// the whitelisted functions, <c>GROUP BY</c>, <c>ORDER BY</c> over aliases — is emitted once by
/// the compiler, so a dialect can only vary the handful of things it must. The less a dialect can
/// decide, the less there is to get wrong on the engine nobody tested against today.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>Name used in configuration and in audit records.</summary>
    string Name { get; }

    /// <summary>The escape character used with <c>LIKE</c> patterns.</summary>
    char LikeEscapeCharacter => '\\';

    /// <summary>Quotes an identifier so it survives case folding and reserved words.</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>Renders a reference to the parameter called <paramref name="name"/>.</summary>
    string ParameterReference(string name);

    /// <summary>Renders <paramref name="expression"/> truncated to <paramref name="grain"/>.</summary>
    string TruncateToGrain(string expression, TimeGrain grain);

    /// <summary>Appends the engine's row-limit clause.</summary>
    void AppendRowLimit(StringBuilder sql, int limit);
}
