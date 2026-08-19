using System.Net;
using Nltsql.Core.Queries;

namespace Nltsql.Infrastructure.Cube;

/// <summary>Cube returned an error response.</summary>
public sealed class CubeApiException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>
/// A query failed validation against the live semantic model.
/// </summary>
/// <remarks>
/// Typically a saved query whose members were renamed or removed in the
/// semantic layer. Carrying the errors lets the UI say which field broke
/// instead of showing a generic failure.
/// </remarks>
public sealed class SemanticQueryException(ValidationResult validation)
    : Exception(validation.Summary)
{
    public ValidationResult Validation { get; } = validation;
}
