namespace NuvTools.DataProvider.Client;

/// <summary>
/// How to reach one API on the Nuv Tools Data Provider.
/// </summary>
/// <remarks>
/// One registration per API. Two APIs means two calls to
/// <see cref="ServiceCollectionExtensions.AddNuvToolsDataProvider"/>, each with its own name, because
/// each has its own base address, its own allowance and — usually — its own token.
/// </remarks>
public class DataProviderOptions
{
    /// <summary>The data plane's address. Only ever changed to point at a test environment.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.nuvtools.com/");

    /// <summary>
    /// The API's code on the platform — <c>geography</c>, and the first segment of every address it
    /// answers on.
    /// </summary>
    public string ApiCode { get; set; } = string.Empty;

    /// <summary>
    /// Supplies the access token, per request.
    /// </summary>
    /// <remarks>
    /// <b>A callback and not a string, so the token never has to live in configuration.</b> It can
    /// come from Key Vault, from a secret manager, or from a cache that renews it — and because it
    /// is asked for on every call, rotating it takes effect without restarting the process. Cache it
    /// yourself if fetching is expensive; this client calls it each time and does not second-guess
    /// where it comes from.
    /// </remarks>
    public Func<CancellationToken, ValueTask<string>>? TokenProvider { get; set; }

    /// <summary>
    /// How long one attempt may take, before retries. Kept under the platform's own ceiling so a
    /// timeout here means the call really is slow rather than that we gave up early.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Whether to retry the calls that are safe to retry.
    /// </summary>
    /// <remarks>
    /// <b>Only idempotent methods, and never a 4xx the caller has to fix.</b> A retried POST is a
    /// duplicate in somebody else's system, and a spent monthly quota does not come back within any
    /// sane wait — see <see cref="DataProviderError.IsTransient"/>, which is what the pipeline asks.
    /// </remarks>
    public bool EnableRetries { get; set; } = true;

    internal Uri ApiBaseAddress =>
        new(BaseAddress, $"{ApiCode.Trim('/')}/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiCode))
            throw new InvalidOperationException(
                $"{nameof(DataProviderOptions)}.{nameof(ApiCode)} is required; it is the API's code on the platform, for example \"geography\".");

        if (TokenProvider is null)
            throw new InvalidOperationException(
                $"{nameof(DataProviderOptions)}.{nameof(TokenProvider)} is required; it supplies the access token for each call.");

        if (!BaseAddress.IsAbsoluteUri)
            throw new InvalidOperationException(
                $"{nameof(DataProviderOptions)}.{nameof(BaseAddress)} must be an absolute URL.");
    }
}
