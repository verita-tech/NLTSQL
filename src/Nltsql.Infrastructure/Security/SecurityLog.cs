using Microsoft.Extensions.Logging;

namespace Nltsql.Infrastructure.Security;

internal static partial class SecurityLog
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "TLS-Zertifikatsprüfung für \"{ClientName}\" ist deaktiviert. Die Verbindung ist "
                + "verschlüsselt, aber der Server wird nicht mehr überprüft — ein Angreifer im Netzwerk "
                + "kann sich dazwischenschalten. Nur für Testzwecke geeignet; besser die Zertifikate "
                + "unter TrustedCertificateThumbprints hinterlegen.")]
    public static partial void CertificateValidationDisabled(ILogger logger, string clientName);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "TLS für \"{ClientName}\": {Count} hinterlegte Zertifikat-Fingerabdrücke werden zusätzlich akzeptiert.")]
    public static partial void CertificatePinned(ILogger logger, string clientName, int count);
}
