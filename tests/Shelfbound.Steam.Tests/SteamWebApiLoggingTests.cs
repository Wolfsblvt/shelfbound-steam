using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shelfbound.Mcp;
using Shelfbound.Mcp.Logging;
using Shelfbound.Steam.Web;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SteamWebApiLoggingTests
{
    private const string DummyApiKey = "dummy-steam-web-api-key";

    [Fact]
    public async Task T12_does_not_emit_the_dummy_key_for_any_steam_web_api_outcome()
    {
        foreach (RequestOutcome outcome in Enum.GetValues<RequestOutcome>())
        {
            string logs = await CaptureLogsAsync(outcome);

            logs.Contains(DummyApiKey, StringComparison.Ordinal).ShouldBeFalse(
                $"The {outcome} path must not disclose the API key.");
            logs.Contains("key=***", StringComparison.Ordinal).ShouldBeTrue(
                $"The {outcome} path should retain a redacted query parameter for diagnosis.");
            logs.Contains("System.Net.Http.HttpClient.SteamWebApi", StringComparison.Ordinal).ShouldBeFalse(
                "The named Steam Web API client's framework Information logs must be suppressed.");
            logs.Contains("Steam request scope", StringComparison.Ordinal).ShouldBeTrue(
                $"The {outcome} path must capture and redact logging scopes.");

            AssertOutcomeSpecificLog(outcome, logs);
        }
    }

    [Theory]
    [InlineData("key")]
    [InlineData("api_key")]
    [InlineData("api-key")]
    [InlineData("apikey")]
    [InlineData("token")]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    public void Masks_supported_secret_query_parameters(string parameterName)
    {
        bool wasMasked = SecretMasking.TryMask(
            $"https://example.invalid/?{parameterName}={DummyApiKey}&safe=value",
            out string masked);

        wasMasked.ShouldBeTrue();
        masked.ShouldBe($"https://example.invalid/?{parameterName}=***&safe=value");
    }

    private static void AssertOutcomeSpecificLog(RequestOutcome outcome, string logs)
    {
        string requiredText = outcome switch
        {
            RequestOutcome.HttpError => "HTTP error exception detail",
            RequestOutcome.Timeout => "Timeout exception detail",
            RequestOutcome.Redirect => "Redirect record from",
            RequestOutcome.Exception => "Unhandled exception detail",
            _ => "Success record",
        };

        logs.Contains(requiredText, StringComparison.Ordinal).ShouldBeTrue(
            $"The {outcome} path must include its message, exception, or redirect record in the captured logs.");
    }

    private static async Task<string> CaptureLogsAsync(RequestOutcome outcome)
    {
        var output = new StringWriter();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new RedactingStderrLoggerProvider(output, includeScopes: true));
            logging.AddSteamWebApiLogging();
        });
        services.AddSteamWebApiClient()
            .ConfigurePrimaryHttpMessageHandler(serviceProvider => new ProbeHandler(
                outcome,
                serviceProvider.GetRequiredService<ILogger<ProbeHandler>>()));

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        ISteamWebApiClient client = serviceProvider.GetRequiredService<ISteamWebApiClient>();

        if (outcome is RequestOutcome.Success)
        {
            IReadOnlyList<OwnedGame> games = await client.GetOwnedGamesAsync("76561198000000000", DummyApiKey);
            games.ShouldBeEmpty();
        }
        else
        {
            await Should.ThrowAsync<Exception>(() => client.GetOwnedGamesAsync("76561198000000000", DummyApiKey));
        }

        return output.ToString();
    }

    private sealed class ProbeHandler(RequestOutcome outcome, ILogger<ProbeHandler> logger) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri requestUri = request.RequestUri ?? throw new InvalidOperationException("The Steam request URI is required.");
            using IDisposable? scope = logger.BeginScope("Steam request scope {Uri}", requestUri);

            return outcome switch
            {
                RequestOutcome.Success => SucceedAsync(requestUri),
                RequestOutcome.HttpError => FailWithHttpErrorAsync(requestUri),
                RequestOutcome.Timeout => FailWithTimeoutAsync(requestUri),
                RequestOutcome.Redirect => RedirectAsync(requestUri),
                RequestOutcome.Exception => FailWithExceptionAsync(requestUri),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        private Task<HttpResponseMessage> SucceedAsync(Uri requestUri)
        {
            logger.LogWarning("Success record for {Uri}", requestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = new { games = Array.Empty<object>() } }),
            });
        }

        private Task<HttpResponseMessage> FailWithHttpErrorAsync(Uri requestUri)
        {
            var exception = new HttpRequestException($"HTTP error exception detail for {requestUri}");
            logger.LogWarning(exception, "HTTP error record for {Uri}", requestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = $"Failure for {requestUri}",
            });
        }

        private Task<HttpResponseMessage> FailWithTimeoutAsync(Uri requestUri)
        {
            var exception = new TaskCanceledException($"Timeout exception detail for {requestUri}");
            logger.LogError(exception, "Timeout record for {Uri}", requestUri);
            return Task.FromException<HttpResponseMessage>(exception);
        }

        private Task<HttpResponseMessage> RedirectAsync(Uri requestUri)
        {
            var redirectUri = new UriBuilder(requestUri) { Host = "redirect.example" }.Uri;
            logger.LogWarning("Redirect record from {Source} to {Location}", requestUri, redirectUri);

            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = redirectUri;
            return Task.FromResult(response);
        }

        private Task<HttpResponseMessage> FailWithExceptionAsync(Uri requestUri)
        {
            var exception = new InvalidOperationException($"Unhandled exception detail for {requestUri}");
            logger.LogError(exception, "Exception record for {Uri}", requestUri);
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }

    private enum RequestOutcome
    {
        Success,
        HttpError,
        Timeout,
        Redirect,
        Exception,
    }
}
