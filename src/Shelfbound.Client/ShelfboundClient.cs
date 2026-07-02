using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shelfbound.Core;
using Shelfbound.Core.Model;

namespace Shelfbound.Client;

public enum UploadStatus { Success, Throttled, Unauthorized, Error }

/// <summary>The outcome of an upload, with a human message for throttled/error cases.</summary>
public sealed record UploadResult(UploadStatus Status, int GameCount, string? Message, int? RetryAfterSeconds)
{
    public bool Ok => Status == UploadStatus.Success;
}

/// <summary>The signed-in owner's plan entitlements (mirrors the API's /auth/entitlements shape).</summary>
public sealed record Entitlements(string Plan, bool AutoSync, int MinUploadIntervalSeconds, int MaxDevices);

/// <summary>The signed-in Shelfbound account, as reported by <c>/auth/me</c>. Steam fields are only
/// populated for interactive (cookie) sign-ins; over a device token they may be null.</summary>
public sealed record AccountInfo(string AccountId, string? SteamId, string? DisplayName);

/// <summary>
/// HTTP client for a Shelfbound server: upload a snapshot, and read the signed-in account and entitlements.
/// Used by the CLI and the tray agent. Authenticates with a Bearer API token.
/// </summary>
public sealed class ShelfboundClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public ShelfboundClient(string serverBaseUrl, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<UploadResult> UploadAsync(SnapshotDocument snapshot, CancellationToken ct = default)
    {
        string json = SnapshotSerializer.Serialize(snapshot, indented: false);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("ingest", content, ct);
        }
        catch (Exception ex)
        {
            return new UploadResult(UploadStatus.Error, 0, ex.Message, null);
        }

        if (response.IsSuccessStatusCode)
            return new UploadResult(UploadStatus.Success, snapshot.Games.Count, null, null);

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new UploadResult(UploadStatus.Throttled, 0,
                "Too soon for your plan. Automatic/frequent sync is a Pro/Lifetime feature.",
                (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds),
            HttpStatusCode.Unauthorized => new UploadResult(UploadStatus.Unauthorized, 0,
                "Invalid or missing API token.", null),
            _ => new UploadResult(UploadStatus.Error, 0, $"Server returned {(int)response.StatusCode}.", null),
        };
    }

    public async Task<Entitlements?> GetEntitlementsAsync(CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage response = await _http.GetAsync("auth/entitlements", ct);
            if (!response.IsSuccessStatusCode)
                return null;
            return JsonSerializer.Deserialize<Entitlements>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The signed-in account behind the current token, or null if unauthenticated/unreachable.</summary>
    public async Task<AccountInfo?> GetAccountAsync(CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage response = await _http.GetAsync("auth/me", ct);
            if (!response.IsSuccessStatusCode)
                return null;
            return JsonSerializer.Deserialize<AccountInfo>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
