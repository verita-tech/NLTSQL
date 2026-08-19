using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Nltsql.Infrastructure.Configuration;

namespace Nltsql.Infrastructure.Security;

/// <summary>
/// Builds the server-certificate check for an outbound HTTPS client.
/// </summary>
public static class CertificateValidation
{
    /// <summary>
    /// Returns a validation callback for the given policy, or
    /// <c>null</c> when the platform's normal validation should apply.
    /// </summary>
    /// <remarks>
    /// Returning <c>null</c> matters: it leaves the default chain
    /// validation in place rather than replacing it with a callback that
    /// merely reimplements it. Any custom policy here can only ever be
    /// more permissive than the default, so the unconfigured case must
    /// not go through this code at all.
    /// </remarks>
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? CreateCallback(
        TlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsCustomised)
        {
            return null;
        }

        if (options.DangerousAcceptAnyServerCertificate)
        {
            return static (_, _, _, _) => true;
        }

        // Captured once: HttpClientFactory rebuilds the handler — and
        // with it this callback — every couple of minutes, so nothing
        // here may have a side effect or do avoidable work.
        var pinned = Normalise(options.TrustedCertificateThumbprints);

        return (_, certificate, _, errors) => IsPinned(certificate, errors, pinned);
    }

    /// <summary>
    /// Accepts a certificate that the chain rejects only when its
    /// thumbprint is one of the pinned ones.
    /// </summary>
    private static bool IsPinned(X509Certificate2? certificate, SslPolicyErrors errors, HashSet<string> pinned)
    {
        // A certificate that validates normally needs no exception.
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // A name mismatch is not something a thumbprint should excuse:
        // the certificate may be genuine but issued for another host,
        // which is exactly the confusion an attacker would exploit.
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)
            || certificate is null)
        {
            return false;
        }

        return pinned.Contains(Normalise(certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)))
            || pinned.Contains(Normalise(certificate.Thumbprint));
    }

    private static HashSet<string> Normalise(IEnumerable<string> thumbprints) =>
        thumbprints
            .Select(Normalise)
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Strips the separators tools print thumbprints with.</summary>
    private static string Normalise(string thumbprint) =>
        new(thumbprint.Where(char.IsLetterOrDigit).ToArray());
}
