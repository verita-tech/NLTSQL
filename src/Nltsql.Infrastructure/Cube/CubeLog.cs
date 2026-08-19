using Microsoft.Extensions.Logging;

namespace Nltsql.Infrastructure.Cube;

/// <summary>
/// Source-generated log messages for the Cube client.
/// </summary>
/// <remarks>
/// Generated delegates avoid boxing and formatting work on paths that
/// run for every query.
/// </remarks>
internal static partial class CubeLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Semantisches Modell geladen: {ViewCount} Datenbereiche für Mandant {TenantId}.")]
    public static partial void ModelLoaded(ILogger logger, int viewCount, string tenantId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Cube meldet \"Continue wait\" (Versuch {Attempt}); Abfrage wird erneut gesendet.")]
    public static partial void ContinueWait(ILogger logger, int attempt);
}
