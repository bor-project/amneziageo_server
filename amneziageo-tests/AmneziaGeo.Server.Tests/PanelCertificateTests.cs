using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class PanelCertificateTests
{
    [Fact]
    public void PublicDetailsIncludeTheWholeChainAndMatchTheServiceWidget()
    {
        using var files = new Certificates();
        var source = WebCertificate.Of(files.Chain, files.Key)!;
        var response = new PanelCertificates(source).Read();
        Assert.True(response.Enabled);
        Assert.Equal(3, response.Certificates.Count);
        var leaf = response.Certificates[0];
        Assert.Equal("panel.example.test", leaf.Name);
        Assert.Equal("CN=panel.example.test", leaf.Subject);
        Assert.Equal("CN=Test Intermediate", leaf.Issuer);
        Assert.Equal(["DNS:panel.example.test", "DNS:*.office.example.test", "IP:127.0.0.1", "IP:::1", "email:admin@example.test", "URI:https://panel.example.test/"], leaf.SubjectAlternativeNames);
        Assert.Equal("1.2.840.113549.1.1.1", leaf.KeyOid);
        Assert.Equal(2048, leaf.KeyBits);
        Assert.Equal(TimeSpan.Zero, leaf.NotBefore.Offset);
        Assert.Equal(TimeSpan.Zero, leaf.NotAfter.Offset);
        Assert.NotEmpty(leaf.SignatureAlgorithm);
        Assert.Matches("^[0-9A-F]{64}$", leaf.Sha256);
        var widget = ServiceCertificates.Read(files.Chain, NullLogger.Instance);
        Assert.Equal(widget.Name, leaf.Name);
        Assert.Equal(widget.Until, leaf.NotAfter);
        Assert.Equal(256, response.Certificates[1].KeyBits);
        Assert.Equal("CN=Test Root", response.Certificates[2].Subject);
    }

    [Theory]
    [InlineData("", HttpStatusCode.Unauthorized)]
    [InlineData("viewer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.Forbidden)]
    [InlineData("password", HttpStatusCode.Forbidden)]
    public async Task EveryRouteRequiresPanelAdministrationRights(string role, HttpStatusCode expected)
    {
        using var files = new Certificates();
        await using var server = await Server.StartAsync(files).ConfigureAwait(false);
        foreach (var route in new[] { "", "leaf.pem", "chain.pem" })
        {
            using var response = await server.GetAsync(route, role).ConfigureAwait(false);
            Assert.Equal(expected, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.DoesNotContain("BEGIN CERTIFICATE", text);
            Assert.DoesNotContain("panel.example.test", text);
            Assert.DoesNotContain(files.Folder, text);
        }
    }

    [Fact]
    public async Task TlsDownloadsMatchTheServedLeafAndNeverExportPrivateKeysOrOtherPemText()
    {
        using var files = new Certificates();
        await using var server = await Server.StartAsync(files, secure: true).ConfigureAwait(false);
        using var info = await server.GetAsync("", "admin").ConfigureAwait(false);
        info.EnsureSuccessStatusCode();
        Assert.True(info.Headers.CacheControl!.NoStore);
        var data = (await info.Content.ReadFromJsonAsync<PanelCertificateResponse>().ConfigureAwait(false))!;
        Assert.Equal(server.ServedSha256, data.Certificates[0].Sha256);
        using var leaf = await server.GetAsync("leaf.pem", "admin").ConfigureAwait(false);
        using var chain = await server.GetAsync("chain.pem", "admin").ConfigureAwait(false);
        foreach (var (response, count, filename) in new[] { (leaf, 1, "panel-certificate.pem"), (chain, 3, "panel-chain.pem") })
        {
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("application/x-pem-file", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains(filename, response.Content.Headers.ContentDisposition!.ToString());
            var pem = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.DoesNotContain("PRIVATE KEY", pem);
            Assert.DoesNotContain("test preamble", pem);
            var certificates = new X509Certificate2Collection();
            try
            {
                certificates.ImportFromPem(pem);
                Assert.Equal(count, certificates.Count);
                Assert.All(certificates.Cast<X509Certificate2>(), one => Assert.False(one.HasPrivateKey));
                Assert.Equal(data.Certificates[0].Sha256, certificates[0].GetCertHashString(HashAlgorithmName.SHA256));
            }
            finally
            {
                foreach (var one in certificates)
                {
                    one.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task InspectionKeepsTheRunningIdentityWhenSavedSettingsOrQueryPathsDiffer()
    {
        using var files = new Certificates();
        using var other = new Certificates("other.example.test");
        await using var server = await Server.StartAsync(files, pending: other.Chain).ConfigureAwait(false);
        using var response = await server.GetAsync("?path=" + Uri.EscapeDataString(other.Chain), "admin").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<PanelCertificateResponse>().ConfigureAwait(false))!;
        Assert.Equal("panel.example.test", data.Certificates[0].Name);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.DoesNotContain(other.Folder, text);
        Assert.DoesNotContain("other.example.test", text);
    }

    [Fact]
    public void FileRenewalUpdatesDetailsAndPublicChainTogether()
    {
        using var files = new Certificates();
        var source = WebCertificate.Of(files.Chain, files.Key)!;
        var panel = new PanelCertificates(source);
        var before = panel.Read();
        files.Rotate("renewed.example.test");
        var after = panel.Read();
        Assert.NotEqual(before.Certificates[0].Sha256, after.Certificates[0].Sha256);
        Assert.Equal("renewed.example.test", after.Certificates[0].Name);
        using var exported = X509Certificate2.CreateFromPem(panel.PublicChain()[0]);
        Assert.Equal(source.Current().GetCertHashString(HashAlgorithmName.SHA256), exported.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal(after.Certificates[0].Sha256, exported.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task AnUnavailableFileReturnsAnErrorWithoutLeakingPathsOrServingAnOldExport()
    {
        using var files = new Certificates();
        await using var server = await Server.StartAsync(files).ConfigureAwait(false);
        File.Delete(files.Chain);
        foreach (var route in new[] { "", "leaf.pem", "chain.pem" })
        {
            using var response = await server.GetAsync(route, "admin").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.Contains("certificate-unreadable", text);
            Assert.DoesNotContain(files.Folder, text);
            Assert.DoesNotContain("PRIVATE KEY", text);
            Assert.DoesNotContain("BEGIN CERTIFICATE", text);
        }
    }

    [Fact]
    public async Task APlainPanelHasNoDownloadableCertificate()
    {
        await using var server = await Server.StartAsync(null).ConfigureAwait(false);
        using var response = await server.GetAsync("", "admin").ConfigureAwait(false);
        var data = (await response.Content.ReadFromJsonAsync<PanelCertificateResponse>().ConfigureAwait(false))!;
        Assert.False(data.Enabled);
        Assert.Empty(data.Certificates);
        foreach (var route in new[] { "leaf.pem", "chain.pem" })
        {
            using var missing = await server.GetAsync(route, "admin").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Theory]
    [InlineData(-20, -10)]
    [InlineData(10, 20)]
    public void InspectionRetainsExpiredOrFutureDates(int start, int end)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=clock.example.test", key, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(start), now.AddDays(end));
        var details = CertificateDetails.Of(certificate);
        Assert.Equal(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), details.NotBefore);
        Assert.Equal(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), details.NotAfter);
        Assert.Empty(details.SubjectAlternativeNames);
        Assert.Equal(256, details.KeyBits);
    }

    private sealed class Certificates : IDisposable
    {
        private readonly ECDsa _rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa _issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _issuer;
        public string Folder { get; } = Directory.CreateTempSubdirectory("ag772-cert-").FullName;
        public string Chain => Path.Combine(Folder, "combined.pem");
        public string Key => Chain;

        /// <summary>
        /// ctor
        /// </summary>
        public Certificates(string host = "panel.example.test")
        {
            var now = DateTimeOffset.UtcNow;
            var root = new CertificateRequest("CN=Test Root", _rootKey, HashAlgorithmName.SHA256);
            root.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            root.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            _root = root.CreateSelfSigned(now.AddDays(-30), now.AddYears(2));
            var issuer = new CertificateRequest("CN=Test Intermediate", _issuerKey, HashAlgorithmName.SHA256);
            issuer.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            issuer.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var intermediate = issuer.Create(_root, now.AddDays(-20), now.AddYears(1), RandomNumberGenerator.GetBytes(16));
            _issuer = intermediate.CopyWithPrivateKey(_issuerKey);
            Rotate(host);
        }

        public void Rotate(string host)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(host);
            names.AddDnsName("*.office.example.test");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            names.AddEmailAddress("admin@example.test");
            names.AddUri(new Uri("https://" + host + "/"));
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            var now = DateTimeOffset.UtcNow;
            using var leaf = request.Create(_issuer.SubjectName, X509SignatureGenerator.CreateForECDsa(_issuerKey),
                now.AddDays(-1), now.AddDays(30), RandomNumberGenerator.GetBytes(16));
            File.WriteAllText(Chain, "test preamble\n" + leaf.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem()
                + "\n" + _issuer.ExportCertificatePem() + "\n" + _root.ExportCertificatePem());
        }

        public void Dispose()
        {
            _issuer.Dispose();
            _root.Dispose();
            _issuerKey.Dispose();
            _rootKey.Dispose();
            Directory.Delete(Folder, true);
        }
    }

    private sealed class Server(WebApplication app, HttpClient api, TokenIssuer issuer, ECDsa signing) : IAsyncDisposable
    {
        public string ServedSha256 { get; private set; } = "";

        public static async Task<Server> StartAsync(Certificates? files, bool secure = false, string pending = "")
        {
            var certificate = files is null ? null : WebCertificate.Of(files.Chain, files.Key);
            var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var issuer = new TokenIssuer(signing, new AuthOptions());
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
            {
                if (secure)
                {
                    listen.UseHttps(options => options.ServerCertificateSelector = (_, _) => certificate!.Current());
                }
            }));
            builder.Services.AddSingleton<ITokenIssuer>(issuer);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(new PanelCertificates(certificate));
            builder.Services.AddSingleton(PanelDefaults.Settings with { Certificate = pending });
            var app = builder.Build();
            app.UseBearer();
            app.MapPanelCertificates();
            await app.StartAsync().ConfigureAwait(false);
            var expected = certificate?.Current().GetCertHashString(HashAlgorithmName.SHA256);
            var served = "";
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, leaf, _, _) =>
                {
                    served = leaf?.GetCertHashString(HashAlgorithmName.SHA256) ?? "";
                    return served == expected;
                },
            };
            var api = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single() + "/api/panel/certificate/") };
            var server = new Server(app, api, issuer, signing);
            if (secure)
            {
                using var warm = await api.GetAsync("").ConfigureAwait(false);
                server.ServedSha256 = served;
            }
            return server;
        }

        public async Task<HttpResponseMessage> GetAsync(string path, string role)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (role.Length > 0)
            {
                var scopes = role switch
                {
                    "admin" => Scopes.All,
                    "operator" => [Scopes.ReadState, Scopes.ManageClients],
                    "password" => [Scopes.ChangePassword],
                    _ => [Scopes.ReadState],
                };
                var token = issuer.Issue(new Principal(1, role, AuthScheme.Password, 1, scopes.ToHashSet(), role), DateTimeOffset.UtcNow);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return await api.SendAsync(request).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            issuer.Dispose();
            signing.Dispose();
        }
    }
}
