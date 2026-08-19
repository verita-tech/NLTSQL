using Microsoft.Extensions.Logging;

namespace Nltsql.Infrastructure.Planning;

internal static partial class PlannerLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Frage in Abfrage auf Datenbereich {View} übersetzt (Versuche: {Attempts}).")]
    public static partial void Planned(ILogger logger, string view, int attempts);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Frage als nicht beantwortbar eingestuft: {Reason}")]
    public static partial void NotAnswerable(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Geplante Abfrage war in Versuch {Attempt} ungültig: {Errors}")]
    public static partial void ValidationFailed(ILogger logger, int attempt, string errors);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "Antwort des Sprachmodells war kein gültiges JSON: {Error}")]
    public static partial void UnreadableResponse(ILogger logger, string error);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Ollama unter {BaseUrl} nicht erreichbar: {Error}")]
    public static partial void ServerUnreachable(ILogger logger, string baseUrl, string error);
}
