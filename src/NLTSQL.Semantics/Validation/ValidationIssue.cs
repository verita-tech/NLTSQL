namespace NLTSQL.Semantics.Validation;

/// <summary>How seriously to take a validation finding.</summary>
public enum IssueSeverity
{
    /// <summary>The model still loads, but something is likely to hurt answer quality.</summary>
    Warning,

    /// <summary>The model is unusable as it stands.</summary>
    Error,
}

/// <summary>A single finding about a semantic model.</summary>
/// <param name="Severity">Whether this blocks loading.</param>
/// <param name="Code">Stable machine-readable code, for filtering and for tests.</param>
/// <param name="Path">Dotted path to the offending element, for example <c>auftrag.measures.umsatz</c>.</param>
/// <param name="Message">Human-readable explanation, addressed to whoever maintains the model.</param>
public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Path, string Message)
{
    /// <summary>Creates an error.</summary>
    public static ValidationIssue Error(string code, string path, string message) =>
        new(IssueSeverity.Error, code, path, message);

    /// <summary>Creates a warning.</summary>
    public static ValidationIssue Warning(string code, string path, string message) =>
        new(IssueSeverity.Warning, code, path, message);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{this.Severity.ToString().ToUpperInvariant()} {this.Code} at '{this.Path}': {this.Message}";
}

/// <summary>Convenience helpers over a set of findings.</summary>
public static class ValidationIssueExtensions
{
    /// <summary>Whether any finding blocks use of the model.</summary>
    public static bool HasErrors(this IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues.Any(issue => issue.Severity is IssueSeverity.Error);
    }
}
