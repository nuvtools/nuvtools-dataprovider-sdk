using NuvTools.Common.ResultWrapper;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NuvTools.DataProvider.Client.UnitTests;

/// <summary>
/// The responses the data plane actually writes, rebuilt here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The body is produced the same way the gateway produces it</b> — serializing
/// <c>Result.Fail(message)</c>, which is declared to return <c>IResult</c>, so System.Text.Json
/// walks the interface's members. That detail changes the JSON, and writing the expected string by
/// hand here would have baked in whatever we assumed rather than what is sent.
/// </para>
/// <para>
/// This client and the gateway share no code by design, so these fixtures are a copy of a contract
/// rather than the contract itself. A test that fails here after a gateway change is the copy doing
/// its job.
/// </para>
/// </remarks>
internal static class GatewayRefusals
{
    public static HttpResponseMessage CredentialRefused() =>
        Refusal(HttpStatusCode.Unauthorized, "The access token was not accepted.");

    public static HttpResponseMessage ValidationUnavailable() =>
        Refusal(HttpStatusCode.ServiceUnavailable, "Access credentials cannot be verified right now. Please retry shortly.");

    /// <summary>The monthly allowance, spent. Publishes the three rate-limit headers.</summary>
    public static HttpResponseMessage QuotaExceeded(long limit = 100, long secondsUntilReset = 1_771_200)
    {
        var response = Refusal(HttpStatusCode.TooManyRequests, "This subscription has used its monthly allowance.");

        var resetsAt = DateTimeOffset.UtcNow.AddSeconds(secondsUntilReset).ToUnixTimeSeconds();

        response.Headers.TryAddWithoutValidation(DataProviderHeaders.RateLimitLimit, limit.ToString(CultureInfo.InvariantCulture));
        response.Headers.TryAddWithoutValidation(DataProviderHeaders.RateLimitRemaining, "0");
        response.Headers.TryAddWithoutValidation(DataProviderHeaders.RateLimitReset, resetsAt.ToString(CultureInfo.InvariantCulture));
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(secondsUntilReset));

        return response;
    }

    /// <summary>
    /// The per-second limiter. Identical status, <b>no</b> allowance headers — which is the only
    /// thing that tells it apart from a spent quota.
    /// </summary>
    public static HttpResponseMessage RateLimited(int retryAfterSeconds = 1)
    {
        var response = Refusal(HttpStatusCode.TooManyRequests, "Too many requests with this access token.");

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));

        return response;
    }

    public static HttpResponseMessage RequestTooLarge() =>
        Refusal(HttpStatusCode.RequestEntityTooLarge, "The request body is larger than this API accepts.");

    public static HttpResponseMessage BackendUnreachable(string apiCode) =>
        Refusal(HttpStatusCode.BadGateway,
            $"The API '{apiCode}' could not be reached. This is the provider's service, not the platform.");

    public static HttpResponseMessage BackendTimedOut(string apiCode) =>
        Refusal(HttpStatusCode.GatewayTimeout,
            $"The API '{apiCode}' did not answer in time. This is the provider's service; the platform reached it and waited.");

    /// <summary>The route was removed while YARP still held it: a 404 with no body at all.</summary>
    public static HttpResponseMessage NotRoutable() => new(HttpStatusCode.NotFound)
    {
        Content = new ByteArrayContent([])
    };

    /// <summary>A provider's own 404 — its answer to a question, with a body it wrote.</summary>
    public static HttpResponseMessage ProvidersOwnNotFound() => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent("""{"error":"no such country"}""", Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Ok(long? limit = null, long? remaining = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json")
        };

        if (limit is not null && remaining is not null)
        {
            response.Headers.TryAddWithoutValidation(DataProviderHeaders.RateLimitLimit, limit.Value.ToString(CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation(DataProviderHeaders.RateLimitRemaining, remaining.Value.ToString(CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation(
                DataProviderHeaders.RateLimitReset,
                DateTimeOffset.UtcNow.AddDays(9).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }

    private static HttpResponseMessage Refusal(HttpStatusCode status, string message) => new(status)
    {
        // Exactly as the gateway writes it: JsonSerializer.Serialize over the IResult the factory
        // returns, content type "application/json" with no charset.
        Content = new StringContent(
            JsonSerializer.Serialize(Result.Fail(message)), Encoding.UTF8, "application/json")
    };
}
