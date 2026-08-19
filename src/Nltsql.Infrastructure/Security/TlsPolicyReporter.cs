using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Security;

/// <summary>
/// Reports the certificate policy of every outbound client once, at
/// startup.
/// </summary>
/// <remarks>
/// Deliberately not logged from the validation callback: that callback
/// is rebuilt each time HttpClientFactory rotates its handler, so the
/// warning would reappear every couple of minutes and read as an ongoing
/// event rather than what it is — a property of the deployment, decided
/// before the first request.
/// <para>
/// It exists so that switching validation off cannot pass unnoticed into
/// an environment it was never meant for.
/// </para>
/// </remarks>
public sealed class TlsPolicyReporter(
    IOptions<CubeOptions> cube,
    IOptions<MetabaseOptions> metabase,
    IOptions<PlannerOptions> planner,
    ILogger<TlsPolicyReporter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Report("Cube", cube.Value.Tls);
        Report("Metabase", metabase.Value.Tls);
        Report("Ollama", planner.Value.Tls);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Report(string clientName, TlsOptions tls)
    {
        if (tls.DangerousAcceptAnyServerCertificate)
        {
            SecurityLog.CertificateValidationDisabled(logger, clientName);
        }
        else if (tls.TrustedCertificateThumbprints.Count > 0)
        {
            SecurityLog.CertificatePinned(logger, clientName, tls.TrustedCertificateThumbprints.Count);
        }
    }
}
