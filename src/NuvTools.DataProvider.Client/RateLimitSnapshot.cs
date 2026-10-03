namespace NuvTools.DataProvider.Client;

/// <summary>
/// What the platform last said about a subscription's monthly allowance.
/// </summary>
/// <remarks>
/// <para>
/// Published on <b>every</b> answer to a metered call, not only on a refusal — which is what lets a
/// caller slow down before it runs out rather than after. Absent when the subscription has no
/// ceiling, when the credential is a test one, and when the platform could not read the counter: a
/// figure we could not read is not one to publish, so <see cref="Remaining"/> is never a guess.
/// </para>
/// <para>
/// This is the <b>monthly</b> allowance. The per-second rate limit is a different mechanism and says
/// nothing here — see <see cref="DataProviderErrorType.RateLimited"/>.
/// </para>
/// </remarks>
/// <param name="Limit">Calls the subscription may bill in the month.</param>
/// <param name="Remaining">How many are left, never below zero.</param>
/// <param name="ResetsOnUtc">When the allowance renews: the start of the next UTC calendar month.</param>
public readonly record struct RateLimitSnapshot(long Limit, long Remaining, DateTimeOffset ResetsOnUtc)
{
    /// <summary>Reads the three headers, or <see langword="null"/> when the platform sent none.</summary>
    /// <remarks>
    /// All three or nothing: the platform writes them together, and a partial set would mean
    /// something has rewritten them in between — in which case none of them can be trusted.
    /// </remarks>
    public static RateLimitSnapshot? From(HttpResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (Value(headers, DataProviderHeaders.RateLimitLimit) is not { } limit
            || Value(headers, DataProviderHeaders.RateLimitRemaining) is not { } remaining
            || Value(headers, DataProviderHeaders.RateLimitReset) is not { } reset)
            return null;

        return new RateLimitSnapshot(limit, remaining, DateTimeOffset.FromUnixTimeSeconds(reset));
    }

    private static long? Value(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values)
        && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
