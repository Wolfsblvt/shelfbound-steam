using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Shelfbound.Client;
using Shelfbound.Steam.Steam;
using Shelfbound.Tray;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class ConnectFlowTests
{
    private const string Code = "sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string State = "test-state";
    private const string Token = "sb_dummy-upload-token-that-must-not-leak";
    private const string DeviceName = "Living-room Deck";

    [Fact]
    public async Task HappyPathRedeemsOnceStoresOnlyTheTokenAndPreservesTheExactBinding()
    {
        using var temp = new TempDirectory();
        string tokenPath = Path.Combine(temp.Path, "token.bin");
        var redeemHandler = new SingleUseRedeemHandler();
        using var redeemHttp = new HttpClient(redeemHandler)
        {
            BaseAddress = new Uri("https://api.example.test/"),
        };
        using var callbackHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        string? launchUrl = null;
        string? callbackUrl = null;
        Task<HttpResponseMessage>? callbackTask = null;
        var dependencies = new ConnectFlowDependencies
        {
            OpenBrowser = url =>
            {
                launchUrl = url;
                Dictionary<string, string> launchQuery = ParseQuery(new Uri(url).Query);
                callbackUrl = $"{launchQuery["cb"]}?code={Uri.EscapeDataString(Code)}" +
                    $"&state={Uri.EscapeDataString(launchQuery["state"])}";
                callbackTask = callbackHttp.GetAsync(callbackUrl);
            },
            RedeemCodeAsync = (request, ct) =>
                ShelfboundClient.RedeemConnectCodeAsync(redeemHttp, request, ct),
            StoreToken = token => TokenStore.Save(token, tokenPath),
            GetFreePort = GetFreePort,
            CreateState = () => State,
            Timeout = TimeSpan.FromSeconds(10),
        };

        ConnectFlowResult? result = await ConnectFlow.RunAsync(
            "https://app.example.test/",
            $"  {DeviceName}  ",
            dependencies);

        result.ShouldNotBeNull();
        Task<HttpResponseMessage> pendingCallback = callbackTask.ShouldNotBeNull();
        using HttpResponseMessage callbackResponse = await pendingCallback;
        callbackResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        string callbackBody = await callbackResponse.Content.ReadAsStringAsync();

        launchUrl.ShouldNotBeNull();
        callbackUrl.ShouldNotBeNull();
        Dictionary<string, string> launchFields = ParseQuery(new Uri(launchUrl).Query);
        launchFields.Keys.Order().ShouldBe(["cb", "device", "state"]);
        launchFields["device"].ShouldBe(DeviceName);
        launchFields["state"].ShouldBe(State);
        launchFields["cb"].ShouldStartWith("http://127.0.0.1:");
        launchFields["cb"].ShouldEndWith("/");

        Dictionary<string, string> callbackFields = ParseQuery(new Uri(callbackUrl).Query);
        callbackFields.Keys.Order().ShouldBe(["code", "state"]);
        callbackFields["code"].ShouldBe(Code);
        callbackFields["state"].ShouldBe(State);

        redeemHandler.CallCount.ShouldBe(1, "the flow must redeem a one-time code once, without retry");
        redeemHandler.FirstRequest.ShouldNotBeNull();
        redeemHandler.FirstRequest.ShouldBe(new ConnectCodeRedemptionRequest(
            Code,
            launchFields["cb"],
            DeviceName,
            State,
            ClientNonce: null));
        redeemHandler.FirstBody.ShouldContain("\"clientNonce\":null");
        redeemHandler.AuthorizationWasPresent.ShouldBeFalse();
        redeemHandler.CookieWasPresent.ShouldBeFalse();

        result.DeviceName.ShouldBe(DeviceName);
        result.GetType().GetProperties().Select(property => property.Name).ShouldNotContain("Token");
        TokenStore.Load(tokenPath).ShouldBe(Token);

        string[] urlAndCallbackRecords =
        [
            launchUrl,
            callbackUrl,
            redeemHandler.FirstRequestUri!,
            redeemHandler.FirstBody,
            callbackBody,
            result.ToString(),
        ];
        foreach (string record in urlAndCallbackRecords)
            record.ShouldNotContain(
                Token,
                customMessage: "the bearer belongs only in the redemption body and token store");

        ConnectCodeRedemptionException secondRedemption =
            await Should.ThrowAsync<ConnectCodeRedemptionException>(() =>
                ShelfboundClient.RedeemConnectCodeAsync(redeemHttp, redeemHandler.FirstRequest));

        redeemHandler.CallCount.ShouldBe(2, "a failed second redemption must not be retried");
        secondRedemption.Message.ShouldNotContain(Token);
        TokenStore.Load(tokenPath).ShouldBe(Token);
    }

    [Fact]
    public async Task InvalidDeviceNameFailsBeforeOpeningTheBrowserOrListener()
    {
        bool dependencyInvoked = false;
        var dependencies = new ConnectFlowDependencies
        {
            OpenBrowser = _ => dependencyInvoked = true,
            RedeemCodeAsync = (_, _) => throw new InvalidOperationException("must not redeem"),
            StoreToken = _ => dependencyInvoked = true,
            GetFreePort = () => { dependencyInvoked = true; return 49152; },
            CreateState = () => { dependencyInvoked = true; return State; },
            Timeout = TimeSpan.FromSeconds(1),
        };

        await Should.ThrowAsync<ArgumentException>(() => ConnectFlow.RunAsync(
            "https://app.example.test",
            new string('x', 201),
            dependencies));

        dependencyInvoked.ShouldBeFalse("invalid binding data must fail before any callback or HTTP work");
    }

    [Fact]
    public void DeviceNameNormalizationIsSharedByConnectAndSnapshotIdentity()
    {
        DeviceIdentity.NormalizeName($"  {DeviceName}\t").ShouldBe(DeviceName);
        DeviceIdentity.NormalizeName(" \t ").ShouldBe(Environment.MachineName.Trim());

        Should.Throw<ArgumentException>(() => DeviceIdentity.NormalizeName(new string('x', 201)));
        Should.Throw<ArgumentException>(() => DeviceIdentity.NormalizeName("Deck\0"));
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(field => field.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.Ordinal);

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class SingleUseRedeemHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public int CallCount { get; private set; }
        public ConnectCodeRedemptionRequest? FirstRequest { get; private set; }
        public string FirstBody { get; private set; } = "";
        public string? FirstRequestUri { get; private set; }
        public bool AuthorizationWasPresent { get; private set; }
        public bool CookieWasPresent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (CallCount == 1)
            {
                FirstBody = body;
                FirstRequestUri = request.RequestUri?.AbsoluteUri;
                AuthorizationWasPresent = request.Headers.Authorization is not null;
                CookieWasPresent = request.Headers.Contains("Cookie");
                FirstRequest = JsonSerializer.Deserialize<ConnectCodeRedemptionRequest>(body, JsonOptions);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        token = Token,
                        tokenType = "Bearer",
                        scopes = new[] { "device:upload" },
                        deviceName = DeviceName,
                        expiresAt = DateTimeOffset.UtcNow.AddDays(90),
                    }),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                // A response body must never leak through the sanitized exception.
                Content = JsonContent.Create(new { token = Token, error = "already used" }),
            };
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"shelfbound-tray-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* test cleanup only */ }
        }
    }
}
