using System.Net;
using Shelfbound.Client;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class ShelfboundClientTests
{
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
