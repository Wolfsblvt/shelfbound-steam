using System.Net;
using Shelfbound.Client;
using Shelfbound.Core.Model;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class ShelfboundClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UploadSurfacesDeviceNameBindingFailures(HttpStatusCode responseStatus)
    {
        var handler = new StatusHandler(responseStatus);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new ShelfboundClient(http, "dummy-upload-token");

        UploadResult result = await client.UploadAsync(CreateSnapshot("Living-room Deck"));

        result.Status.ShouldBe(UploadStatus.DeviceNameMismatch);
        result.Message.ShouldNotBeNull().ShouldContain("device name");
        handler.RequestUri.ShouldBe("https://api.example.test/ingest");
        handler.RequestBody.ShouldContain("\"name\":\"Living-room Deck\"");
    }

    [Theory]
    [InlineData("", "Bearer", new[] { "device:upload" }, "Living-room Deck")]
    [InlineData("sb_dummy", "bearer", new[] { "device:upload" }, "Living-room Deck")]
    [InlineData("sb_dummy", "Bearer", new[] { "device:upload", "library:read" }, "Living-room Deck")]
    [InlineData("sb_dummy", "Bearer", new[] { "device:upload" }, "Other device")]
    public async Task RedeemFailsClosedOnMalformedAuthority(
        string token,
        string tokenType,
        string[] scopes,
        string responseDeviceName)
    {
        using var http = new HttpClient(new JsonResponseHandler(HttpStatusCode.OK, new
        {
            token,
            tokenType,
            scopes,
            deviceName = responseDeviceName,
            expiresAt = DateTimeOffset.UtcNow.AddDays(90),
        }))
        {
            BaseAddress = new Uri("https://api.example.test/"),
        };
        var request = new ConnectCodeRedemptionRequest(
            "sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "http://127.0.0.1:49152/",
            "Living-room Deck",
            "state",
            ClientNonce: null);

        ConnectCodeRedemptionException exception =
            await Should.ThrowAsync<ConnectCodeRedemptionException>(() =>
                ShelfboundClient.RedeemConnectCodeAsync(http, request));

        exception.Message.ShouldBe("Connect redemption returned a malformed response.");
        if (token.Length > 0)
            exception.Message.ShouldNotContain(token);
    }

    private static SnapshotDocument CreateSnapshot(string deviceName) => new()
    {
        SchemaVersion = "0.5.0",
        SnapshotId = Guid.NewGuid().ToString("D"),
        CreatedAt = DateTimeOffset.UtcNow,
        Source = new SnapshotSource
        {
            Tool = "shelfbound-tray-tests",
            ToolVersion = "1.0.0",
            Platform = OsPlatform.Windows,
        },
        Device = new SnapshotDevice
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = deviceName,
            Type = DeviceType.Unknown,
            Os = OsPlatform.Windows,
        },
        Stats = new SnapshotStats
        {
            LibraryCount = 0,
            InstalledGameCount = 0,
            TotalSizeOnDiskBytes = 0,
        },
    };

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = System.Net.Http.Json.JsonContent.Create(body),
            });
    }
}
