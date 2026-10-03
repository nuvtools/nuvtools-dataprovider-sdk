using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace NuvTools.DataProvider.Client;

/// <summary>
/// Registers a client for one API on the Nuv Tools Data Provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a named <see cref="HttpClient"/> based at the API's address on the data plane, carrying
    /// the access token and a retry policy that knows which of the platform's refusals are worth
    /// retrying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is named after the API's code, so several APIs coexist:
    /// <c>IHttpClientFactory.CreateClient("geography")</c>. Its base address ends in a slash and the
    /// API's code, so a relative <c>v1/countries</c> resolves the way a reader expects.
    /// </para>
    /// <para>
    /// <b>Retries are the reason this is not four lines of your own.</b> Only idempotent methods are
    /// retried, never a 4xx the caller has to fix, and — the one that is easy to get wrong — never a
    /// spent monthly quota, whose <c>Retry-After</c> is measured in weeks. The per-second rate limit
    /// looks identical on the wire and <i>is</i> retried, after the delay the platform asked for.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configure">Sets the API's code and where the token comes from.</param>
    public static IHttpClientBuilder AddNuvToolsDataProvider(
        this IServiceCollection services, Action<DataProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DataProviderOptions();

        configure(options);
        options.Validate();

        var builder = services.AddHttpClient(options.ApiCode, client =>
        {
            client.BaseAddress = options.ApiBaseAddress;
            client.Timeout = options.Timeout;
        });

        builder.AddHttpMessageHandler(() => new DataProviderTokenHandler(options));

        if (options.EnableRetries) builder.AddDataProviderResilience();


        return builder;
    }

    /// <summary>
    /// The standard resilience pipeline, narrowed to what is safe against a metered marketplace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Idempotent methods only.</b> The data plane forwards a POST to somebody else's system, and
    /// a retried POST is a duplicate order, a duplicate charge, a duplicate row — the platform's own
    /// gateway refuses to retry for the same reason.
    /// </para>
    /// <para>
    /// <b>A spent quota is not transient.</b> Both it and the per-second limit answer 429, and only
    /// the allowance headers tell them apart; retrying the first sits in a loop until the month
    /// rolls over.
    /// </para>
    /// </remarks>
    private static void AddDataProviderResilience(this IHttpClientBuilder builder) =>
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));

            // The platform states how long to wait, and on the per-second limiter that answer is
            // better than any backoff curve we could invent.
            options.Retry.ShouldRetryAfterHeader = true;

            // A refusal is cheap and says nothing about the platform's health, so it must not count
            // toward opening the breaker — otherwise one caller's bad token takes out their own
            // client for everyone sharing it.
            options.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));
        });

    /// <summary>
    /// Whether one outcome is worth sending again. Internal so it can be asserted directly — it is
    /// the rule this package exists for, and a bug in it is silent.
    /// </summary>
    internal static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TaskCanceledException) return true;

        if (outcome.Result is not { } response) return false;

        if (response.IsSuccessStatusCode) return false;

        if (!IsIdempotent(response.RequestMessage?.Method)) return false;

        return response.StatusCode switch
        {
            // Told apart by the allowance headers: present means the month is spent, and waiting
            // will not help until it renews.
            HttpStatusCode.TooManyRequests => RateLimitSnapshot.From(response.Headers) is null,

            HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.InternalServerError => true,

            _ => false
        };
    }

    /// <summary>
    /// The methods HTTP defines as safe to send again. <c>PUT</c> and <c>DELETE</c> are idempotent
    /// by definition even though they change things — sending either twice leaves the same state.
    /// </summary>
    private static bool IsIdempotent(HttpMethod? method) =>
        method is not null
        && (method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options
            || method == HttpMethod.Trace
            || method == HttpMethod.Put
            || method == HttpMethod.Delete);
}
