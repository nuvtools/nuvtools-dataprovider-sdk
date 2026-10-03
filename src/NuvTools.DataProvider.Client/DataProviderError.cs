namespace NuvTools.DataProvider.Client;

/// <summary>
/// A refusal the platform wrote, read into the fields a caller acts on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The message is the platform's and is localized.</b> It follows the <c>Accept-Language</c> the
/// call was made with, so it is written for a person to read and never for code to match on. Branch
/// on <see cref="Type"/>.
/// </para>
/// <para>
/// <see cref="ApiCode"/> is filled only where the platform names the API — the backend failures,
/// where whose outage it is happens to be the whole question.
/// </para>
/// </remarks>
/// <param name="Type">What went wrong, in terms a caller can act on.</param>
/// <param name="StatusCode">The status the platform answered with.</param>
/// <param name="Message">The platform's own words, localized, for a person to read.</param>
/// <param name="ApiCode">Which API, when the platform named one.</param>
/// <param name="RequestId">The identifier to quote to support.</param>
/// <param name="RetryAfter">How long the platform asked the caller to wait, when it said.</param>
/// <param name="RateLimit">The monthly allowance as the platform last reported it.</param>
public sealed record DataProviderError(
    DataProviderErrorType Type,
    HttpStatusCode StatusCode,
    string? Message,
    string? ApiCode,
    string? RequestId,
    TimeSpan? RetryAfter,
    RateLimitSnapshot? RateLimit)
{
    /// <summary>
    /// Whether the platform refused this call, as opposed to the provider's API answering something
    /// the caller did not want.
    /// </summary>
    /// <remarks>
    /// <b>An unrecognised status is the provider's answer, not ours.</b> The data plane turns a
    /// backend failure into 502 or 504 before it reaches a consumer, so a 404, a 422 or a 500 that
    /// arrives with any other shape was written by the provider's own API and passed through. Those
    /// are answers to be read, and treating them as platform refusals would send a caller chasing a
    /// subscription problem that does not exist.
    /// </remarks>
    public bool IsPlatformRefusal => Type != DataProviderErrorType.Unknown;

    /// <summary>
    /// Whether retrying the identical call could succeed without anything changing.
    /// </summary>
    /// <remarks>
    /// <b><see cref="DataProviderErrorType.QuotaExceeded"/> is deliberately false.</b> Its
    /// <see cref="RetryAfter"/> is the time until the UTC month rolls over — weeks, not seconds — so
    /// a caller that treated it like any other 429 would sit in a retry loop until the next month or
    /// give up believing the platform is broken. This property is what the resilience pipeline asks,
    /// which is why the two 429s are separated at all.
    /// </remarks>
    public bool IsTransient => Type is DataProviderErrorType.PlatformUnavailable
        or DataProviderErrorType.RateLimited
        or DataProviderErrorType.ProviderUnreachable
        or DataProviderErrorType.ProviderTimedOut;

    public override string ToString() =>
        $"{Type} ({(int)StatusCode}){(ApiCode is null ? "" : $" on '{ApiCode}'")}: {Message}"
        + (RequestId is null ? "" : $" [request {RequestId}]");
}
