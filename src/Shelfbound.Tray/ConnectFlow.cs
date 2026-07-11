using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Shelfbound.Client;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Tray;

/// <summary>A completed native-connect handshake. The bearer is deliberately absent.</summary>
public sealed record ConnectFlowResult(string DeviceName, DateTimeOffset ExpiresAt);

/// <summary>
/// Native-app connect handshake: listen on an exact numeric-loopback URI, send a cryptographic state
/// through the browser, redeem the returned one-time code out of band, and store the upload-only token.
/// A bearer is never accepted from or returned to browser navigation.
/// </summary>
public static class ConnectFlow
{
    public static Task<ConnectFlowResult?> RunAsync(
        string webAppUrl,
        string serverBaseUrl,
        string deviceName,
        CancellationToken ct = default)
    {
        var dependencies = new ConnectFlowDependencies
        {
            OpenBrowser = Browser.Open,
            RedeemCodeAsync = (request, token) =>
                ShelfboundClient.RedeemConnectCodeAsync(serverBaseUrl, request, token),
            StoreToken = TokenStore.Save,
            GetFreePort = FreePort,
            CreateState = CreateState,
            Timeout = TimeSpan.FromMinutes(3),
        };
        return RunAsync(webAppUrl, deviceName, dependencies, ct);
    }

    internal static async Task<ConnectFlowResult?> RunAsync(
        string webAppUrl,
        string deviceName,
        ConnectFlowDependencies dependencies,
        CancellationToken ct = default)
    {
        string boundDeviceName = DeviceIdentity.NormalizeName(deviceName);
        string state = dependencies.CreateState();
        ValidateState(state);

        int port = dependencies.GetFreePort();
        string callback = $"http://127.0.0.1:{port}/";
        if (!LoopbackRedirectUri.TryCreate(callback, out LoopbackRedirectUri? redirectUri) || redirectUri is null)
            throw new InvalidOperationException("Could not create a valid numeric-loopback callback URI.");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.Value);
        listener.Start();

        using var timeout = new CancellationTokenSource(dependencies.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using CancellationTokenRegistration registration = linked.Token.Register(() =>
        {
            try { listener.Stop(); } catch { /* already stopping */ }
        });

        HttpListenerContext? context = null;
        try
        {
            string launchUrl = $"{webAppUrl.TrimEnd('/')}/connect" +
                $"?cb={Uri.EscapeDataString(redirectUri.Value)}" +
                $"&device={Uri.EscapeDataString(boundDeviceName)}" +
                $"&state={Uri.EscapeDataString(state)}";
            dependencies.OpenBrowser(launchUrl);

            context = await listener.GetContextAsync();
            if (context.Request.RemoteEndPoint?.Address is not { } remoteAddress ||
                !IPAddress.IsLoopback(remoteAddress) ||
                !LoopbackCallback.TryRead(
                    context.Request.HttpMethod,
                    context.Request.IsSecureConnection,
                    context.Request.Headers["Host"],
                    context.Request.RawUrl,
                    redirectUri,
                    state,
                    out string? code) ||
                code is null)
            {
                await TryRespondAsync(context, ok: false);
                return null;
            }

            var request = new ConnectCodeRedemptionRequest(
                code,
                redirectUri.Value,
                boundDeviceName,
                state,
                ClientNonce: null);
            ConnectCodeRedemptionResponse redemption =
                await dependencies.RedeemCodeAsync(request, linked.Token);

            // Persist before acknowledging success. The bearer never appears in the result, URL, or log.
            dependencies.StoreToken(redemption.Token);
            await TryRespondAsync(context, ok: true);
            return new ConnectFlowResult(redemption.DeviceName, redemption.ExpiresAt);
        }
        catch
        {
            if (context is not null)
                await TryRespondAsync(context, ok: false);
            return null;
        }
        finally
        {
            try { listener.Stop(); } catch { /* already stopped */ }
        }
    }

    internal static string CreateState() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static void ValidateState(string state)
    {
        if (string.IsNullOrWhiteSpace(state) ||
            state.Length > 256 ||
            !string.Equals(state, state.Trim(), StringComparison.Ordinal) ||
            state.Any(char.IsControl))
        {
            throw new InvalidOperationException("The generated connect state is invalid.");
        }
    }

    private static async Task TryRespondAsync(HttpListenerContext context, bool ok)
    {
        try
        {
            string body = ok
                ? "<html><body style='font-family:system-ui;background:#0f172a;color:#e2e8f0;text-align:center;padding-top:80px'><h2>Shelfbound connected</h2><p>You can close this tab and return to the app.</p></body></html>"
                : "<html><body style='font-family:system-ui;text-align:center;padding-top:80px'><h2>Connection failed</h2><p>You can close this tab.</p></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        catch
        {
            // Browser acknowledgement is best-effort; it cannot change a completed redemption/store.
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal sealed record ConnectFlowDependencies
{
    public required Action<string> OpenBrowser { get; init; }
    public required Func<ConnectCodeRedemptionRequest, CancellationToken, Task<ConnectCodeRedemptionResponse>>
        RedeemCodeAsync { get; init; }
    public required Action<string> StoreToken { get; init; }
    public required Func<int> GetFreePort { get; init; }
    public required Func<string> CreateState { get; init; }
    public required TimeSpan Timeout { get; init; }
}
