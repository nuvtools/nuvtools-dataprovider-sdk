using NuvTools.Common.ResultWrapper;

namespace NuvTools.DataProvider.Client;

/// <summary>
/// Reads a platform refusal out of a response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here throws on a failed status by itself.</b> A 404 from a provider's own API is an
/// ordinary answer to an ordinary question, and a client that threw on it would make every caller
/// wrap every call. Ask for the error, or call <see cref="EnsureSuccessAsync"/> when you want the
/// exception.
/// </para>
/// </remarks>
public static class DataProviderResponseExtensions
{
    /// <summary>
    /// The platform's refusal, or <see langword="null"/> when the response did not come from one.
    /// </summary>
    /// <remarks>
    /// <b>A successful status is never an error, however odd its body.</b> Everything else is
    /// classified by status and headers; a status this client does not recognise comes back as
    /// <see cref="DataProviderErrorType.Unknown"/> rather than being forced into a neighbour.
    /// </remarks>
    public static async Task<DataProviderError?> ToDataProviderErrorAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode) return null;

        var type = Classify(response);

        // The body is the platform's IResult envelope. Read through NuvTools.Common rather than a
        // parser of our own: it is the shape every other call in this platform already answers with,
        // and it tolerates a body that is not one at all — which is what a bare 404 sends.
        var result = await response.ToResultAsync(cancellationToken).ConfigureAwait(false);

        var message = result.Messages.Count > 0 ? result.Messages[0].Title : null;

        return new DataProviderError(
            type,
            response.StatusCode,
            string.IsNullOrWhiteSpace(message) ? null : message,
            ApiCodeOf(response),
            RequestIdOf(response),
            RetryAfterOf(response),
            RateLimitSnapshot.From(response.Headers));
    }

    /// <summary>
    /// Throws <see cref="DataProviderException"/> when <b>the platform</b> refused the call, and
    /// returns the response untouched otherwise.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately does less than <c>EnsureSuccessStatusCode</c>.</b> A provider's own 404, 422
    /// or 500 is that API answering a question, and a client that threw on it would make every
    /// caller wrap every call in a try. Only the refusals this package can name —
    /// <see cref="DataProviderError.IsPlatformRefusal"/> — become exceptions; read the status
    /// yourself for everything else.
    /// </remarks>
    public static async Task<HttpResponseMessage> EnsureSuccessAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (await response.ToDataProviderErrorAsync(cancellationToken).ConfigureAwait(false)
            is { IsPlatformRefusal: true } error)
            throw new DataProviderException(error);

        return response;
    }

    /// <summary>
    /// The monthly allowance as of this response, on a success as well as a refusal — which is what
    /// lets a caller slow down before it runs out.
    /// </summary>
    public static RateLimitSnapshot? RateLimit(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return RateLimitSnapshot.From(response.Headers);
    }

    /// <summary>The identifier to quote to support, echoed on every answer.</summary>
    public static string? RequestId(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return RequestIdOf(response);
    }

    private static DataProviderErrorType Classify(HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => DataProviderErrorType.CredentialRefused,
            HttpStatusCode.ServiceUnavailable => DataProviderErrorType.PlatformUnavailable,

            // The two 429s, told apart by the allowance headers: the quota refusal publishes them
            // and the per-second limiter does not. Getting this wrong in either direction is the
            // mistake this package exists to stop — see DataProviderError.IsTransient.
            HttpStatusCode.TooManyRequests =>
                RateLimitSnapshot.From(response.Headers) is not null
                    ? DataProviderErrorType.QuotaExceeded
                    : DataProviderErrorType.RateLimited,

            HttpStatusCode.RequestEntityTooLarge => DataProviderErrorType.RequestTooLarge,
            HttpStatusCode.BadGateway => DataProviderErrorType.ProviderUnreachable,
            HttpStatusCode.GatewayTimeout => DataProviderErrorType.ProviderTimedOut,

            // A routable API's own 404 has a body it wrote; the platform's has none. Content-Length
            // is checked rather than the body read, so this stays cheap and does not consume a
            // stream the caller may still want.
            HttpStatusCode.NotFound when (response.Content.Headers.ContentLength ?? 0) == 0
                => DataProviderErrorType.ApiNotRoutable,

            _ => DataProviderErrorType.Unknown
        };

    private static string? RequestIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(DataProviderHeaders.RequestId, out var values)
            ? values.FirstOrDefault()
            : null;

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null) return null;

        if (retryAfter.Delta is { } delta) return delta;

        // The platform always sends delta-seconds; a date is handled anyway because a proxy in
        // between is entitled to rewrite it as one.
        return retryAfter.Date is { } date
            ? Max(date - DateTimeOffset.UtcNow, TimeSpan.Zero)
            : null;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    /// <summary>
    /// The API the platform named, taken from the request rather than the message.
    /// </summary>
    /// <remarks>
    /// The message names the API too, but it is localized prose — parsing it would tie this client
    /// to a sentence somebody is free to reword. The first path segment is the API code by
    /// construction, because that is how the data plane addresses an API.
    /// </remarks>
    private static string? ApiCodeOf(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is not { } uri) return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 ? segments[0] : null;
    }
}
