using System.Net;
using System.Text;
using Shouldly;
using Shelfbound.Client;
using Shelfbound.Core;
using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Tests;

public class ShelfboundClientTests
{
    [Fact]
    public async Task Upload_projects_the_body_and_surfaces_a_success_warning()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"schemaVersion":"0.5.0","summary":{},"warning":"Switching devices deleted the previous Free snapshot."}""");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.Success);
        result.ErrorCode.ShouldBe(UploadErrorCode.None);
        result.GameCount.ShouldBe(1);
        result.Warning.ShouldBe("Switching devices deleted the previous Free snapshot.");
        result.Response.ShouldBeOfType<UploadResponseV1>();
        result.Response!.SchemaVersion.ShouldBe("0.5.0");
        handler.RequestBody.ShouldNotBeNull();
        handler.RequestBody.ShouldNotContain("steamAccounts");
        handler.RequestBody.ShouldNotContain("synthetic-login");
    }

    [Fact]
    public async Task Upload_maps_throttle_body_and_retry_seconds()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests,
            """{"error":"Upload rejected: too soon for your plan.","plan":"Free","retryAfterSeconds":42,"hint":"Wait before retrying."}""",
            retryAfterSeconds: 99);
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.Throttled);
        result.ErrorCode.ShouldBe(UploadErrorCode.TooSoon);
        result.RetryAfterSeconds.ShouldBe(42);
        result.Plan.ShouldBe("Free");
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Wait before retrying.");
    }

    [Fact]
    public async Task Upload_maps_device_cap_conflict()
    {
        var handler = new StubHandler(HttpStatusCode.Conflict,
            """{"error":"Device limit reached for your plan.","plan":"Pro","maxDevices":3,"hint":"Remove a device."}""");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.DeviceLimited);
        result.ErrorCode.ShouldBe(UploadErrorCode.DeviceLimitReached);
        result.Plan.ShouldBe("Pro");
        result.MaxDevices.ShouldBe(3);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Remove a device.");
    }

    [Fact]
    public async Task Upload_maps_missing_upload_scope_as_forbidden()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden,
            """{"error":"This API token cannot upload snapshots."}""");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.Forbidden);
        result.ErrorCode.ShouldBe(UploadErrorCode.InsufficientScope);
        result.Message.ShouldBe("This API token cannot upload snapshots.");
    }

    [Fact]
    public async Task Upload_maps_device_binding_mismatch_separately()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden,
            """{"error":"This device token is bound to a different snapshot device."}""");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.Forbidden);
        result.ErrorCode.ShouldBe(UploadErrorCode.DeviceMismatch);
    }

    [Fact]
    public async Task Upload_maps_invalid_snapshot_payload()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"error":"Invalid snapshot."}""");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.InvalidSnapshot);
        result.ErrorCode.ShouldBe(UploadErrorCode.InvalidSnapshot);
        result.Message.ShouldBe("Invalid snapshot.");
    }

    [Fact]
    public async Task Upload_maps_payload_too_large_without_a_body()
    {
        var handler = new StubHandler(HttpStatusCode.RequestEntityTooLarge, "");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.PayloadTooLarge);
        result.ErrorCode.ShouldBe(UploadErrorCode.PayloadTooLarge);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("payload-size limit");
    }

    [Fact]
    public async Task Upload_maps_unauthorized_separately()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, "");
        using ShelfboundClient client = CreateClient(handler);

        UploadResult result = await client.UploadAsync(LocalSnapshot());

        result.Status.ShouldBe(UploadStatus.Unauthorized);
        result.ErrorCode.ShouldBe(UploadErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_projection_failure_sends_no_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"schemaVersion":"0.5.0","summary":{},"warning":null}""");
        using ShelfboundClient client = CreateClient(handler);
        SnapshotDocument invalid = LocalSnapshot() with { Device = null! };

        UploadResult result = await client.UploadAsync(invalid);

        result.Status.ShouldBe(UploadStatus.Error);
        result.ErrorCode.ShouldBe(UploadErrorCode.ProjectionFailed);
        handler.RequestBody.ShouldBeNull();
    }

    private static ShelfboundClient CreateClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://shelfbound.test/") };
        return new ShelfboundClient(http, "synthetic-token");
    }

    private static SnapshotDocument LocalSnapshot() => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "33333333-3333-3333-3333-333333333333",
        CreatedAt = DateTimeOffset.Parse("2026-07-11T12:00:00+00:00"),
        Source = new SnapshotSource
        {
            Tool = "test",
            ToolVersion = "1.0.0",
            Platform = OsPlatform.Linux,
        },
        Device = new SnapshotDevice
        {
            Id = "44444444-4444-4444-4444-444444444444",
            Name = "Test device",
            Type = DeviceType.SteamDeck,
            Os = OsPlatform.Linux,
        },
        SteamAccounts =
        [
            new SteamAccount
            {
                SteamId64 = "76561198000000001",
                AccountName = "synthetic-login",
                PersonaName = "Synthetic Persona",
            },
        ],
        Games = [new SnapshotGame { AppId = 1, Name = "Test game", Installed = true }],
        Stats = new SnapshotStats
        {
            LibraryCount = 0,
            InstalledGameCount = 1,
            TotalSizeOnDiskBytes = 0,
        },
    };

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string responseBody,
        int? retryAfterSeconds = null) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is { } seconds)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(seconds));
            return response;
        }
    }
}
