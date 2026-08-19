using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Security;
using Shouldly;

namespace Nltsql.Tests.Security;

/// <summary>
/// Exercises the policy against a real self-signed certificate — the
/// exact case it exists for.
/// </summary>
public sealed class CertificateValidationTests
{
    private static X509Certificate2 CreateSelfSigned(string subject = "CN=ollama.intern")
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? Build(
        TlsOptions options) =>
        CertificateValidation.CreateCallback(options);

    [Fact]
    public void Leaves_default_validation_in_place_when_nothing_is_configured()
    {
        // Returning null keeps the platform's chain validation, which is
        // stricter than anything this class could rebuild.
        Build(new TlsOptions()).ShouldBeNull();
    }

    [Fact]
    public void Accepts_anything_when_validation_is_switched_off()
    {
        var callback = Build(new TlsOptions { DangerousAcceptAnyServerCertificate = true }).ShouldNotBeNull();

        using var certificate = CreateSelfSigned();
        using var request = new HttpRequestMessage();

        callback(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
        callback(request, null, null, SslPolicyErrors.RemoteCertificateNameMismatch).ShouldBeTrue();
    }

    [Fact]
    public void Accepts_an_untrusted_certificate_whose_thumbprint_is_pinned()
    {
        using var certificate = CreateSelfSigned();

        var callback = Build(new TlsOptions
        {
            TrustedCertificateThumbprints = [certificate.GetCertHashString(HashAlgorithmName.SHA256)],
        }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        callback(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    [Fact]
    public void Ignores_the_separators_that_tools_print_thumbprints_with()
    {
        using var certificate = CreateSelfSigned();

        var raw = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var colonSeparated = string.Join(':', Enumerable.Range(0, raw.Length / 2)
            .Select(i => raw.Substring(i * 2, 2)));

        var callback = Build(new TlsOptions
        {
            TrustedCertificateThumbprints = [colonSeparated.ToLowerInvariant()],
        }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        // openssl prints AA:BB:CC…; nobody should have to reformat it.
        callback(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_an_untrusted_certificate_that_is_not_pinned()
    {
        using var pinned = CreateSelfSigned();
        using var other = CreateSelfSigned("CN=someone.else");

        var callback = Build(new TlsOptions
        {
            TrustedCertificateThumbprints = [pinned.GetCertHashString(HashAlgorithmName.SHA256)],
        }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        callback(request, other, null, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Fact]
    public void Does_not_let_a_pin_excuse_a_host_name_mismatch()
    {
        using var certificate = CreateSelfSigned();

        var callback = Build(new TlsOptions
        {
            TrustedCertificateThumbprints = [certificate.GetCertHashString(HashAlgorithmName.SHA256)],
        }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        // The certificate may be genuine but issued for a different host.
        // That is precisely the confusion an attacker would rely on, so a
        // thumbprint match must not wave it through.
        callback(request, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch).ShouldBeFalse();
    }

    [Fact]
    public void Accepts_a_certificate_that_validates_normally()
    {
        using var certificate = CreateSelfSigned();

        var callback = Build(new TlsOptions { TrustedCertificateThumbprints = ["irrelevant"] }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        callback(request, certificate, null, SslPolicyErrors.None).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_when_no_certificate_was_presented()
    {
        var callback = Build(new TlsOptions { TrustedCertificateThumbprints = ["abc"] }).ShouldNotBeNull();

        using var request = new HttpRequestMessage();

        callback(request, null, null, SslPolicyErrors.RemoteCertificateNotAvailable).ShouldBeFalse();
    }
}
