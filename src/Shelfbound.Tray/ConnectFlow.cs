using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shelfbound.Tray;

/// <summary>
/// The "connect account" handshake without copy-pasting tokens (OAuth native-app loopback pattern):
/// start a localhost listener, open the browser to the dashboard's connect page with that callback, and
/// wait for it to redirect back with a device token. A random <c>state</c> guards against stray requests.
/// </summary>
public static class ConnectFlow
{
    public static async Task<string?> RunAsync(string webAppUrl, string deviceName, CancellationToken ct = default)
    {
        int port = FreePort();
        string state = Guid.NewGuid().ToString("N");
        string callback = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(callback);
        listener.Start();

        string url = $"{webAppUrl.TrimEnd('/')}/connect" +
            $"?cb={Uri.EscapeDataString(callback)}&device={Uri.EscapeDataString(deviceName)}&state={state}";
        OpenBrowser(url);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using CancellationTokenRegistration registration =
            linked.Token.Register(() => { try { listener.Stop(); } catch { /* already stopping */ } });

        try
        {
            HttpListenerContext context = await listener.GetContextAsync();
            string? token = context.Request.QueryString["token"];
            string? returnedState = context.Request.QueryString["state"];
            bool ok = returnedState == state && !string.IsNullOrEmpty(token);

            await RespondAsync(context, ok);
            return ok ? token : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { listener.Stop(); } catch { /* already stopped */ }
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, bool ok)
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

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If the launcher fails the user can paste the URL manually; not worth crashing over.
        }
    }
}
