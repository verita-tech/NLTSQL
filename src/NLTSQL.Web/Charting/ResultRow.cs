namespace NLTSQL.Web.Charting;

/// <summary>
/// One result row, wrapped so it can be a Razor component's item type.
/// </summary>
/// <remarks>
/// The engine hands back rows as <c>IReadOnlyList&lt;object?&gt;</c>, which is the right shape for
/// a result of unknown width. It cannot be used as a Razor generic argument, though: the generated
/// component code has no nullable context, so the annotation on <c>object?</c> fails to compile.
/// Wrapping it costs one allocation per row and keeps the engine's API honest about nullability.
/// </remarks>
/// <param name="Values">The row's values, in result-column order.</param>
public sealed record ResultRow(IReadOnlyList<object?> Values)
{
    /// <summary>The value in the column at <paramref name="index"/>.</summary>
    public object? this[int index] => this.Values[index];
}
