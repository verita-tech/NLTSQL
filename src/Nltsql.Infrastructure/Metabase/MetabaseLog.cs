using Microsoft.Extensions.Logging;

namespace Nltsql.Infrastructure.Metabase;

internal static partial class MetabaseLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Metabase-Frage {CardId} angelegt.")]
    public static partial void CardCreated(ILogger logger, int cardId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Metabase-Frage {CardId} aktualisiert.")]
    public static partial void CardUpdated(ILogger logger, int cardId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Frage {CardId} zu Metabase-Dashboard {DashboardId} hinzugefügt.")]
    public static partial void CardAddedToDashboard(ILogger logger, int cardId, int dashboardId);
}
