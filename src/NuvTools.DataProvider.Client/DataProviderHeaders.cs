namespace NuvTools.DataProvider.Client;

/// <summary>
/// The headers the platform publishes, named once.
/// </summary>
/// <remarks>
/// Restated here rather than shared with the gateway: the two are released separately and a package
/// a customer installs cannot take a project reference into the data plane. That duplication is the
/// same one every deployable in the platform accepts, and it is the price of the consumer being able
/// to upgrade on their own schedule.
/// </remarks>
public static class DataProviderHeaders
{
    /// <summary>Calls the subscription may bill this month.</summary>
    public const string RateLimitLimit = "X-RateLimit-Limit";

    /// <summary>How many of them are left.</summary>
    public const string RateLimitRemaining = "X-RateLimit-Remaining";

    /// <summary>When the allowance renews, as Unix seconds.</summary>
    public const string RateLimitReset = "X-RateLimit-Reset";

    /// <summary>
    /// Echoed back on every answer, and the one thing worth quoting to support. Send your own and
    /// the platform keeps it.
    /// </summary>
    public const string RequestId = "X-Request-Id";
}
