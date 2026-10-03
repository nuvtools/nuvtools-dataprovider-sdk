namespace NuvTools.DataProvider.Client;

/// <summary>
/// What went wrong with a call, in the few outcomes a caller can act on differently.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the platform's refusals, not the provider's.</b> An API that answers 404 for a
/// country nobody has heard of is answering its own question, and this type never appears: only
/// responses the gateway itself wrote are classified here. That is the distinction a hand-rolled
/// <see cref="HttpClient"/> cannot make, because on the wire both are just status codes.
/// </para>
/// <para>
/// The mapping is deliberately narrower than the status codes suggest. The gateway collapses every
/// reason a credential can be refused — unknown, revoked, expired, unsubscribed — into one 401, so
/// that a caller cannot use the difference to discover which tokens exist. This enumeration does not
/// invent the distinction back.
/// </para>
/// </remarks>
public enum DataProviderErrorType
{
    /// <summary>Not a failure this client recognises; read the status code yourself.</summary>
    Unknown = 0,

    /// <summary>
    /// 401 — the credential was not accepted. Missing, unknown, revoked or without a live
    /// subscription: the platform answers all four the same way on purpose, so there is nothing more
    /// specific to tell you. Fix the token; retrying it will not help.
    /// </summary>
    CredentialRefused = 1,

    /// <summary>
    /// 503 — the platform could not verify the credential right now. <b>This is our outage, not your
    /// credential</b>, and it is the one refusal worth retrying unchanged.
    /// </summary>
    PlatformUnavailable = 2,

    /// <summary>
    /// 429 — the subscription has spent its monthly allowance. <see cref="DataProviderError.RetryAfter"/>
    /// is the time until the UTC month rolls over, which can be weeks: <b>this is not something to
    /// sit and retry.</b> Raise the plan's ceiling or wait for the renewal.
    /// </summary>
    QuotaExceeded = 3,

    /// <summary>
    /// 429 — too many requests in too short a time with this token. Unlike
    /// <see cref="QuotaExceeded"/> the allowance is not spent, only the moment is: waiting the
    /// seconds in <see cref="DataProviderError.RetryAfter"/> and retrying is exactly right, and this
    /// client already does it for idempotent calls.
    /// </summary>
    RateLimited = 4,

    /// <summary>413 — the request body is larger than that API accepts.</summary>
    RequestTooLarge = 5,

    /// <summary>
    /// 502 — the provider's backend could not be reached. <b>Their service, not the marketplace</b>;
    /// <see cref="DataProviderError.ApiCode"/> says whose.
    /// </summary>
    ProviderUnreachable = 6,

    /// <summary>
    /// 504 — the provider's backend did not answer in time. The platform reached it and waited.
    /// </summary>
    ProviderTimedOut = 7,

    /// <summary>
    /// 404 with no body — the API is no longer routable, which is what a withdrawn or deleted API
    /// looks like from outside. An API that is routable and answers 404 itself is the provider's own
    /// answer and is never classified as this.
    /// </summary>
    ApiNotRoutable = 8
}
