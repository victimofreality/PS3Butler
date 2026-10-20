using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PS3Butler.Services;

internal static class HttpsDownloadSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "https", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Schannel cannot use CreateSelfSigned's ephemeral key directly. Import
        // a temporary key handle without persisting the key or installing trust.
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        // Trust only this ephemeral fixture certificate, with normal hostname checks.
        // No certificate-store or production-client changes are made.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                cert?.Thumbprint == certificate.Thumbprint &&
                (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
        };
        using var client = new HttpClient(handler);
        var downloader = new DownloadService(client);
        var checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS: {label}");
            checks++;
        }

        await using (var server = new Server(certificate, Ok("Content-Disposition: attachment; filename*=UTF-8''Game%20caf%C3%A9.iso\r\n")))
        {
            var result = await Download(downloader, server.Url, "filename");
            Check(result.Success && Path.GetFileName(result.OutputPath) == "Game café.iso", $"HTTPS honors UTF-8 server filename ({result.Error}; {Path.GetFileName(result.OutputPath)})");
            Check(result.Success && await File.ReadAllTextAsync(result.OutputPath) == "okay", "HTTPS payload preserved");
        }

        foreach (var tlsRedirect in new[] { false, true })
        {
            await using var target = new Server(certificate, Ok());
            await using var redirect = new Server(tlsRedirect ? certificate : null,
                $"HTTP/1.1 302 Found\r\nLocation: {target.Url}Final%20Game.iso\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            var result = await Download(downloader, redirect.Url + "start", tlsRedirect ? "https-redirect" : "http-upgrade");
            Check(result.Success && Path.GetFileName(result.OutputPath) == "Final Game.iso", $"{(tlsRedirect ? "HTTPS" : "HTTP")} redirect uses final HTTPS URL filename");
            Check(target.RequestPath == "/Final%20Game.iso", "redirect reaches intended target path");
        }

        foreach (var code in new[] { 401, 403, 429 })
        {
            await using var server = new Server(certificate, $"HTTP/1.1 {code} Rejected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            var result = await Download(downloader, server.Url + "game.iso", code.ToString());
            Check(!result.Success && result.Error.Contains(code.ToString()), $"HTTPS {code} reports server status");
            Check(!Directory.EnumerateFiles(Path.Combine(root, code.ToString())).Any(), $"HTTPS {code} leaves no file");
        }

        await using (var server = new Server(certificate, "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nshort"))
        {
            var result = await Download(downloader, server.Url + "game.iso", "truncated");
            Check(!result.Success && result.Error.StartsWith("Download failed:"), "truncated TLS transfer reports failure");
            Check(!Directory.EnumerateFiles(Path.Combine(root, "truncated")).Any(), "truncated TLS partial removed");
        }

        foreach (var sendHeaders in new[] { false, true })
        {
            await using var server = new Server(certificate, sendHeaders
                ? "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nshort" : "", holdOpen: true);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var name = sendHeaders ? "cancel-body" : "cancel-headers";
            var pending = downloader.DownloadAsync(server.Url + "game.iso", Path.Combine(root, name), cancellationToken: cancellation.Token);
            await server.ResponseSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!result.Success && result.Error == "Download cancelled.", $"HTTPS cancellation awaiting {(sendHeaders ? "body" : "headers")} settles");
            Check(!Directory.EnumerateFiles(Path.Combine(root, name)).Any(), "HTTPS cancellation removes partial");
        }

        await using (var server = new Server(certificate, Ok()))
        {
            var result = await Download(new DownloadService(), server.Url + "game.iso", "untrusted");
            Check(!result.Success && result.Error.StartsWith("Download failed:"), "default production client rejects untrusted TLS certificate");
            Check(!Directory.EnumerateFiles(Path.Combine(root, "untrusted")).Any(), "certificate rejection leaves no output");
        }
        await using (var server = new Server(certificate, Ok()))
        {
            var result = await Download(downloader, server.Url + "retry.iso", "retry");
            Check(result.Success, "HTTPS client remains usable after failures and cancellation");
        }
        Console.WriteLine($"HTTPS smoke: {checks} checks passed. Fixtures: {root}");

        Task<(bool Success, string OutputPath, string Error)> Download(DownloadService service, string url, string folder) =>
            service.DownloadAsync(url, Path.Combine(root, folder)).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static string Ok(string headers = "") => $"HTTP/1.1 200 OK\r\n{headers}Content-Length: 4\r\nConnection: close\r\n\r\nokay";

    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        public TaskCompletionSource ResponseSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; }
        public string? RequestPath { get; private set; }

        public Server(X509Certificate2? certificate, string response, bool holdOpen = false)
        {
            _listener.Start();
            Url = $"{(certificate is null ? "http" : "https")}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serve = ServeAsync(certificate, response, holdOpen);
        }

        private async Task ServeAsync(X509Certificate2? certificate, string response, bool holdOpen)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var network = client.GetStream();
                using var tls = certificate is null ? null : new SslStream(network, leaveInnerStreamOpen: true);
                if (tls is not null)
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, _stop.Token);
                Stream stream = tls is null ? network : tls;
                using var reader = new StreamReader(stream, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_stop.Token);
                RequestPath = requestLine?.Split(' ').ElementAtOrDefault(1);
                // An untrusted client may close immediately after the handshake.
                if (requestLine is null) return;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                await stream.FlushAsync(_stop.Token);
                ResponseSent.TrySetResult();
                if (holdOpen) await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (AuthenticationException) { /* Expected when the default client rejects fixture trust. */ }
            catch (IOException) { /* Client may terminate a rejected TLS connection. */ }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _serve;
            _stop.Dispose();
        }
    }
}
