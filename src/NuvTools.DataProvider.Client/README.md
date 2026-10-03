# NuvTools.DataProvider.Client

The consumer SDK for the [Nuv Tools Data Provider](https://dataprovider.nuvtools.com) marketplace.
Install it to call an API you have subscribed to.

```bash
dotnet add package NuvTools.DataProvider.Client
```

## Registering

```csharp
services.AddNuvToolsDataProvider(options =>
{
    options.ApiCode = "geography";
    options.TokenProvider = async ct => await secrets.GetAsync("geography-token", ct);
});
```

That gives you a named `HttpClient` — `IHttpClientFactory.CreateClient("geography")` — based at
`https://api.nuvtools.com/geography/`, so a relative path reads the way you would write it:

```csharp
var client = httpClientFactory.CreateClient("geography");
var response = await client.GetAsync("v1/countries");
```

One registration per API. Two APIs means two calls, each with its own code and its own token.

**The token is a callback, not a string.** It is asked for on every call, so it can come from Key
Vault or a secret manager and rotating it takes effect without restarting the process. Cache it
yourself if fetching is expensive.

## Reading what went wrong

```csharp
if (await response.ToDataProviderErrorAsync() is { } error)
{
    switch (error.Type)
    {
        case DataProviderErrorType.CredentialRefused:   // fix the token
        case DataProviderErrorType.QuotaExceeded:       // the month is spent
        case DataProviderErrorType.ProviderUnreachable: // their outage, not ours
        ...
    }
}
```

Or `await response.EnsureSuccessAsync()` to get a `DataProviderException` instead.

| Type | Status | What it means |
|---|---|---|
| `CredentialRefused` | 401 | The token was not accepted. Missing, unknown, revoked or without a live subscription — the platform answers all four identically on purpose, so there is nothing more specific to tell you. |
| `PlatformUnavailable` | 503 | We could not verify your credential right now. **Our problem, not your token** — retry it unchanged. |
| `QuotaExceeded` | 429 | The subscription has spent its monthly allowance. `RetryAfter` is the time until the UTC month rolls over. |
| `RateLimited` | 429 | Too many requests too quickly. The allowance is fine; only the moment is. |
| `RequestTooLarge` | 413 | The body is larger than that API accepts. |
| `ProviderUnreachable` | 502 | The provider's backend could not be reached. `ApiCode` says whose. |
| `ProviderTimedOut` | 504 | The provider's backend did not answer in time. |
| `ApiNotRoutable` | 404 | The API is no longer published. |
| `Unknown` | — | Not a platform refusal: the provider's API answered this itself. |

### The two 429s are not the same, and this is the main reason to use this package

A spent monthly quota and a per-second rate limit are **identical on the wire** — same status, same
`Retry-After` header. Only the `X-RateLimit-*` headers tell them apart, and confusing them is
expensive in both directions: retry a spent quota and you loop for up to a month; give up on a busy
second and you stop for no reason.

This client separates them, and its retry policy follows: `RateLimited` is retried after the delay
the platform asked for, `QuotaExceeded` never is.

### Retries

On by default, and deliberately narrow:

- **Idempotent methods only** — `GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT`, `DELETE`. A retried `POST`
  is a duplicate in the provider's system.
- **Never a 4xx you have to fix**, and never a spent quota.
- 503, 502, 504, 500, 408 and transport failures are retried, honouring `Retry-After`.

Set `options.EnableRetries = false` to bring your own.

## Watching your allowance

Published on every answer to a metered call, not only on the refusal — so you can slow down before
you run out:

```csharp
if (response.RateLimit() is { } allowance && allowance.Remaining < 100)
    logger.LogWarning("{Remaining} calls left until {Reset:u}.", allowance.Remaining, allowance.ResetsOnUtc);
```

Absent when the subscription has no ceiling, when the credential is a test one, and when the platform
could not read the counter — it never guesses a figure.

## Support

Every answer carries `X-Request-Id`, available as `response.RequestId()`. Quote it. Send your own and
the platform keeps it.

## What this package is not

- **It is not a typed client for any particular API.** Generated per-API clients would make Nuv Tools
  responsible for contracts third parties own and change. You get an `HttpClient`; deserialize what
  the provider documents.
- **It does not meter anything.** Calls are counted in the platform's data plane, where the provider
  cannot influence what their own customer is billed for.

## Targets

`net8.0`, `net9.0` and `net10.0`, with **no ASP.NET Core dependency** — usable from a console app, a
worker, a desktop app or a web app alike.
