namespace Nltsql.Infrastructure.Configuration;

/// <summary>
/// How an outbound HTTPS connection treats the server's certificate.
/// </summary>
/// <remarks>
/// Internal services often run with a self-signed or private-CA
/// certificate that the machine does not trust. There are two ways out,
/// and they are not equally good:
/// <list type="bullet">
/// <item><description>
/// <see cref="TrustedCertificateThumbprints"/> pins the specific
/// certificate. The connection stays authenticated — only that one
/// certificate is accepted, so nobody can interpose their own.
/// </description></item>
/// <item><description>
/// <see cref="DangerousAcceptAnyServerCertificate"/> switches the check
/// off entirely. Any certificate is then accepted, including one
/// presented by whoever sits between this app and the service. Use it
/// for a quick trial, not for anything that carries real data.
/// </description></item>
/// </list>
/// Both are off by default: a working default must not be an insecure
/// one.
/// </remarks>
public sealed class TlsOptions
{
    /// <summary>
    /// Accepts every server certificate, valid or not.
    /// </summary>
    /// <remarks>
    /// Named "dangerous" so it cannot be enabled without reading what it
    /// does. It defeats the point of TLS: traffic stays encrypted, but
    /// there is no longer any guarantee about who it is encrypted to.
    /// The app logs a warning at startup for every client that has this
    /// on, so it does not quietly outlive the trial it was meant for.
    /// </remarks>
    public bool DangerousAcceptAnyServerCertificate { get; set; }

    /// <summary>
    /// SHA-256 or SHA-1 thumbprints of certificates to accept even when
    /// the chain cannot be validated.
    /// </summary>
    /// <remarks>
    /// The safe answer to a self-signed certificate. Read the value with
    /// <c>openssl s_client -connect host:port &lt;/dev/null 2&gt;/dev/null |
    /// openssl x509 -fingerprint -sha256 -noout</c>; spaces and colons
    /// are ignored when comparing.
    /// <para>
    /// A pinned certificate has to be replaced here when it is renewed —
    /// that is the cost of pinning, and the reason it is opt-in.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> TrustedCertificateThumbprints { get; set; } = [];

    public bool IsCustomised =>
        DangerousAcceptAnyServerCertificate || TrustedCertificateThumbprints.Count > 0;
}
