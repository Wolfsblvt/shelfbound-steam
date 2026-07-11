using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shelfbound.Core.Model;

namespace Shelfbound.Client;

public enum UploadStatus
{
    Success,
    Throttled,
    Unauthorized,
    Forbidden,
    DeviceLimited,
    InvalidSnapshot,
    PayloadTooLarge,
    Error,
}

/// <summary>Stable client-side codes for the current ingest response contract.</summary>
public enum UploadErrorCode
{
    None,
    ProjectionFailed,
    NetworkError,
    InvalidResponse,
    Unauthorized,
    InsufficientScope,
    DeviceMismatch,
    Forbidden,
    DeviceLimitReached,
    InvalidSnapshot,
    PayloadTooLarge,
    TooSoon,
    ServerError,
}

/// <summary>
/// Version 1 of the server's <c>POST /ingest</c> response body. Success and error responses share
/// this tolerant DTO because older server outcomes omit fields that do not apply.
/// </summary>
public sealed record UploadResponseV1
{
    public string? SchemaVersion { get; init; }
    public JsonElement? Summary { get; init; }
    public string? Warning { get; init; }
    public string? Error { get; init; }
    public string? Plan { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public int? MaxDevices { get; init; }
    public string? Hint { get; init; }
}

/// <summary>A typed upload outcome, including any successful warning and structured error details.</summary>
public sealed record UploadResult
{
    public required UploadStatus Status { get; init; }
    public required UploadErrorCode ErrorCode { get; init; }
    public int GameCount { get; init; }
    public string? Message { get; init; }
    public string? Warning { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? Plan { get; init; }
    public int? MaxDevices { get; init; }
    public UploadResponseV1? Response { get; init; }
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
/// HTTP client for a Shelfbound server: upload a privacy-minimized snapshot, redeem native-connect codes
/// without browser-carried credentials, and read account/entitlements for broad-scope CLI tokens. Used by
/// the CLI and tray agent.
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
        SetAuthorization(token);
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

    /// <summary>Projects a complete local snapshot before any network request is attempted.</summary>
    public Task<UploadResult> UploadAsync(SnapshotDocument snapshot, CancellationToken ct = default)
    {
        try
        {
            return UploadAsync(HostedProjection.Prepare(snapshot), ct);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new UploadResult
            {
                Status = UploadStatus.Error,
                ErrorCode = UploadErrorCode.ProjectionFailed,
                Message = $"Could not create the privacy-minimized upload: {ex.Message}",
            });
        }
    }

    /// <summary>Sends the exact canonical JSON held by a previously previewed hosted upload.</summary>
    public async Task<UploadResult> UploadAsync(HostedUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        using var content = new StringContent(upload.Json, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("ingest", content, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UploadResult
            {
                Status = UploadStatus.Error,
                ErrorCode = UploadErrorCode.NetworkError,
                Message = ex.Message,
            };
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new UploadResult
                {
                    Status = UploadStatus.Error,
                    ErrorCode = UploadErrorCode.NetworkError,
                    Message = ex.Message,
                };
            }

            UploadResponseV1? parsed = TryParseResponse(body);
            if (response.IsSuccessStatusCode)
            {
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.SchemaVersion))
                {
                    return new UploadResult
                    {
                        Status = UploadStatus.Error,
                        ErrorCode = UploadErrorCode.InvalidResponse,
                        Message = "The server returned an invalid upload response.",
                    };
                }

                return new UploadResult
                {
                    Status = UploadStatus.Success,
                    ErrorCode = UploadErrorCode.None,
                    GameCount = upload.GameCount,
                    Warning = parsed.Warning,
                    Response = parsed,
                };
            }

            return MapError(response, parsed);
        }
    }

    public async Task<Entitlements?> GetEntitlementsAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync("auth/entitlements", ct);
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
            using HttpResponseMessage response = await _http.GetAsync("auth/me", ct);
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

    private static UploadResult MapError(HttpResponseMessage response, UploadResponseV1? body)
    {
        string Message(string fallback) => FormatMessage(body, fallback);

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new UploadResult
            {
                Status = UploadStatus.Throttled,
                ErrorCode = UploadErrorCode.TooSoon,
                Message = Message("Upload rejected: too soon for your plan."),
                RetryAfterSeconds = body?.RetryAfterSeconds ?? RetryAfter(response),
                Plan = body?.Plan,
                Response = body,
            },
            HttpStatusCode.Unauthorized => new UploadResult
            {
                Status = UploadStatus.Unauthorized,
                ErrorCode = UploadErrorCode.Unauthorized,
                Message = Message("Invalid or missing API token."),
                Response = body,
            },
            HttpStatusCode.Forbidden => Forbidden(body),
            HttpStatusCode.Conflict => new UploadResult
            {
                Status = UploadStatus.DeviceLimited,
                ErrorCode = UploadErrorCode.DeviceLimitReached,
                Message = Message("Device limit reached for your plan."),
                Plan = body?.Plan,
                MaxDevices = body?.MaxDevices,
                Response = body,
            },
            HttpStatusCode.BadRequest => new UploadResult
            {
                Status = UploadStatus.InvalidSnapshot,
                ErrorCode = UploadErrorCode.InvalidSnapshot,
                Message = Message("The server rejected the snapshot schema or payload."),
                Response = body,
            },
            HttpStatusCode.RequestEntityTooLarge => new UploadResult
            {
                Status = UploadStatus.PayloadTooLarge,
                ErrorCode = UploadErrorCode.PayloadTooLarge,
                Message = Message("The hosted upload exceeds the server's payload-size limit."),
                Response = body,
            },
            _ => new UploadResult
            {
                Status = UploadStatus.Error,
                ErrorCode = UploadErrorCode.ServerError,
                Message = Message($"Server returned {(int)response.StatusCode}."),
                Response = body,
            },
        };
    }

    private static UploadResult Forbidden(UploadResponseV1? body)
    {
        string error = body?.Error ?? "";
        UploadErrorCode code = error.Contains("cannot upload", StringComparison.OrdinalIgnoreCase)
            ? UploadErrorCode.InsufficientScope
            : error.Contains("bound to a different", StringComparison.OrdinalIgnoreCase)
                ? UploadErrorCode.DeviceMismatch
                : UploadErrorCode.Forbidden;

        return new UploadResult
        {
            Status = UploadStatus.Forbidden,
            ErrorCode = code,
            Message = FormatMessage(body, "This token is not allowed to upload this device snapshot."),
            Response = body,
        };
    }

    private static UploadResponseV1? TryParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonSerializer.Deserialize<UploadResponseV1>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatMessage(UploadResponseV1? body, string fallback)
    {
        string? error = body?.Error;
        string? hint = body?.Hint;
        if (string.IsNullOrWhiteSpace(error))
            return string.IsNullOrWhiteSpace(hint) ? fallback : hint;
        return string.IsNullOrWhiteSpace(hint) ? error : $"{error} {hint}";
    }

    private static int? RetryAfter(HttpResponseMessage response)
    {
        double? seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
        return seconds is null ? null : (int)Math.Ceiling(seconds.Value);
    }

    private void SetAuthorization(string token) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public void Dispose() => _http.Dispose();
}
