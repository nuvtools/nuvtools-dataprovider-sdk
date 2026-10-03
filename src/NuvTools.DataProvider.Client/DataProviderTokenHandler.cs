namespace NuvTools.DataProvider.Client;

/// <summary>
/// Puts the access token on every request.
/// </summary>
/// <remarks>
/// <para>
/// A handler rather than a header set once on the <see cref="HttpClient"/>, because the token is
/// fetched per call: that is what lets it come from a store that rotates it without the process
/// being restarted.
/// </para>
/// <para>
/// <b>A token already on the request wins.</b> A caller that set <c>Authorization</c> themselves
/// meant it — most often to use a test credential for one call — and silently replacing it would be
/// the kind of surprise that costs an afternoon.
/// </para>
/// </remarks>
public class DataProviderTokenHandler(DataProviderOptions options) : DelegatingHandler
{
    private readonly DataProviderOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.Authorization is null && _options.TokenProvider is { } provider)
        {
            var token = await provider(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
