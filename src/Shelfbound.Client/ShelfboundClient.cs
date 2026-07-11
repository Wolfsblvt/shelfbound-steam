using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shelfbound.Core;
using Shelfbound.Core.Model;

namespace Shelfbound.Client;

public enum UploadStatus { Success, Throttled, Unauthorized, DeviceNameMismatch, Error }

/// <summary>The outcome of an upload, with a human message for throttled/error cases.</summary>
public sealed record UploadResult(UploadStatus Status, int GameCount, string? Message, int? RetryAfterSeconds)
{
    public bool Ok => Status == UploadStatus.Success;
}

/// <summary>
/// The signed-in owner's plan entitlements for broad-scope CLI credentials. Upload-only tray tokens
/// cannot call this endpoint.
/// </summary>
public sealed record Entitlements(string Plan, bool AutoSync, int MinUploadIntervalSeconds, int MaxDevices);

/// <summary>
/// The signed-in Shelfbound account for broad-scope credentials, as reported by <c>/auth/me</c>.
/// Upload-only tray tokens cannot call this endpoint.
/// </summary>
public sealed record AccountInfo(string AccountId, string? SteamId, string? DisplayName);

/// <summary>The exact native-connect binding sent to <c>/auth/connect/redeem</c>.</summary>
public sealed record ConnectCodeRedemptionRequest(
    string Code,
    string RedirectUri,
    string DeviceName,
    string State,
    string? ClientNonce);

/// <summary>A successfully redeemed upload-only device credential.</summary>
public sealed record ConnectCodeRedemptionResponse(
    string Token,
    string TokenType,
    IReadOnlyList<string> Scopes,
    string DeviceName,
    DateTimeOffset ExpiresAt);

/// <summary>A sanitized native-connect redemption failure. Response bodies are never included.</summary>
public sealed class ConnectCodeRedemptionException(string message) : Exception(message);

/// <summary>
/// HTTP client for a Shelfbound server: upload a snapshot, read account data for broad-scope CLI tokens,
/// and redeem native-connect codes without browser-carried credentials. Used by the CLI and tray agent.
/// </summary>
public sealed class ShelfboundClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public ShelfboundClient(string serverBaseUrl, string token)
        : this(new HttpClient { BaseAddress = CreateBaseAddress(serverBaseUrl) }, token)
    {
    }

    internal ShelfboundClient(HttpClient http, string token)
    {
        _http = http;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Redeems a single native-connect code with no Authorization header, cookies, redirects, or retry.
    /// Any response other than a well-formed <c>200 OK</c> fails closed.
    /// </summary>
    public static async Task<ConnectCodeRedemptionResponse> RedeemConnectCodeAsync(
        string serverBaseUrl,
        ConnectCodeRedemptionRequest request,
        CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        using var http = new HttpClient(handler) { BaseAddress = CreateBaseAddress(serverBaseUrl) };
        return await RedeemConnectCodeAsync(http, request, ct);
    }

    internal static async Task<ConnectCodeRedemptionResponse> RedeemConnectCodeAsync(
        HttpClient http,
        ConnectCodeRedemptionRequest request,
        CancellationToken ct = default)
    {
        ValidateRedemptionRequest(request);
        if (http.DefaultRequestHeaders.Authorization is not null ||
            http.DefaultRequestHeaders.Contains("Cookie"))
        {
            throw new InvalidOperationException("Connect redemption requires an unauthenticated HTTP client.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "auth/connect/redeem")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        using HttpResponseMessage response = await http.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ConnectCodeRedemptionException(
                $"Connect redemption failed with HTTP {(int)response.StatusCode}.");
        }

        ConnectCodeRedemptionPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<ConnectCodeRedemptionPayload>(JsonOptions, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw MalformedRedemptionResponse();
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Token) ||
            !string.Equals(payload.Token, payload.Token.Trim(), StringComparison.Ordinal) ||
            payload.Token.Any(char.IsControl) ||
            !string.Equals(payload.TokenType, "Bearer", StringComparison.Ordinal) ||
            payload.Scopes is not ["device:upload"] ||
            !string.Equals(payload.DeviceName, request.DeviceName, StringComparison.Ordinal) ||
            payload.ExpiresAt is null ||
            payload.ExpiresAt == default(DateTimeOffset))
        {
            throw MalformedRedemptionResponse();
        }

        return new ConnectCodeRedemptionResponse(
            payload.Token,
            payload.TokenType!,
            payload.Scopes,
            payload.DeviceName!,
            payload.ExpiresAt.Value);
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

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return new UploadResult(UploadStatus.Success, snapshot.Games.Count, null, null);

            return response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => new UploadResult(UploadStatus.Throttled, 0,
                    "Too soon for your plan. Automatic/frequent sync is a Pro/Lifetime feature.",
                    (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds),
                HttpStatusCode.Unauthorized => new UploadResult(UploadStatus.Unauthorized, 0,
                    "Invalid or missing API token.", null),
                HttpStatusCode.BadRequest or HttpStatusCode.Forbidden =>
                    new UploadResult(UploadStatus.DeviceNameMismatch, 0,
                        "Snapshot device name does not match this device token. Reconnect this device.", null),
                _ => new UploadResult(UploadStatus.Error, 0, $"Server returned {(int)response.StatusCode}.", null),
            };
        }
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

    private static Uri CreateBaseAddress(string serverBaseUrl) =>
        new(serverBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

    private static void ValidateRedemptionRequest(ConnectCodeRedemptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBoundValue(request.Code, 256, nameof(request.Code));
        ValidateBoundValue(request.DeviceName, 200, nameof(request.DeviceName));
        ValidateBoundValue(request.State, 256, nameof(request.State));

        if (string.IsNullOrEmpty(request.RedirectUri) ||
            request.RedirectUri.Length > 2048 ||
            request.RedirectUri.Any(char.IsControl))
            throw new ArgumentException("The redirect URI is invalid.", nameof(request));
        if (request.ClientNonce is not null)
            ValidateBoundValue(request.ClientNonce, 256, nameof(request.ClientNonce));
    }

    private static void ValidateBoundValue(string value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maxLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException($"The {fieldName} binding value is invalid.", fieldName);
        }
    }

    private static ConnectCodeRedemptionException MalformedRedemptionResponse() =>
        new("Connect redemption returned a malformed response.");

    private sealed record ConnectCodeRedemptionPayload(
        string? Token,
        string? TokenType,
        string[]? Scopes,
        string? DeviceName,
        DateTimeOffset? ExpiresAt);

    public void Dispose() => _http.Dispose();
}
