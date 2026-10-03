using System.Net;

namespace NuvTools.DataProvider.Client.UnitTests;

/// <summary>
/// Turning the platform's refusals into something a caller can branch on.
/// </summary>
/// <remarks>
/// The claim worth testing is the one a reviewer cannot see by reading: that the two 429s come out
/// as different types, and that only one of them is worth retrying.
/// </remarks>
[TestFixture]
public class DataProviderErrorTests
{
    private static HttpResponseMessage From(HttpResponseMessage response, string apiCode = "geography")
    {
        response.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get, new Uri($"https://api.nuvtools.com/{apiCode}/v1/countries"));

        return response;
    }

    [Test]
    public async Task ASuccessIsNeverAnError()
    {
        using var response = From(GatewayRefusals.Ok());

        Assert.That(await response.ToDataProviderErrorAsync(), Is.Null);
    }

    [Test]
    public async Task ARefusedCredentialIsNotDifferentiatedAndSaysSo()
    {
        using var response = From(GatewayRefusals.CredentialRefused());

        var error = await response.ToDataProviderErrorAsync();

        Assert.That(error, Is.Not.Null);
        Assert.That(error.Type, Is.EqualTo(DataProviderErrorType.CredentialRefused));
        Assert.That(error.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // The platform's own words survive the round trip, which is what a person reads.
        Assert.That(error.Message, Is.EqualTo("The access token was not accepted."));

        // Nothing to wait for and nothing to retry: the caller has to change the token.
        Assert.That(error.RetryAfter, Is.Null);
        Assert.That(error.IsTransient, Is.False);
    }

    [Test]
    public async Task AnUnverifiableCredentialIsThePlatformsProblemAndIsTransient()
    {
        using var response = From(GatewayRefusals.ValidationUnavailable());

        var error = await response.ToDataProviderErrorAsync();

        // 503 and not 401: the distinction the gateway makes deliberately, and the one an integrator
        // needs to decide whether to fix their credential or simply try again.
        Assert.That(error!.Type, Is.EqualTo(DataProviderErrorType.PlatformUnavailable));
        Assert.That(error.IsTransient, Is.True);
    }

    [Test]
    public async Task TheTwoRateLimitsAreToldApartAndOnlyOneIsWorthRetrying()
    {
        using var quota = From(GatewayRefusals.QuotaExceeded(limit: 100, secondsUntilReset: 1_771_200));
        using var perSecond = From(GatewayRefusals.RateLimited(retryAfterSeconds: 1));

        var spent = await quota.ToDataProviderErrorAsync();
        var busy = await perSecond.ToDataProviderErrorAsync();

        // Identical on the wire — same status, same Retry-After header name. Only the allowance
        // headers separate them.
        Assert.That(spent!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(busy!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));

        Assert.That(spent.Type, Is.EqualTo(DataProviderErrorType.QuotaExceeded));
        Assert.That(busy.Type, Is.EqualTo(DataProviderErrorType.RateLimited));

        // The whole point: a spent month is not something to sit and retry. Its Retry-After is three
        // weeks, and a caller treating it like the busy-second case would loop until the month turns.
        Assert.That(spent.IsTransient, Is.False);
        Assert.That(busy.IsTransient, Is.True);

        Assert.That(spent.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(1_771_200)));
        Assert.That(busy.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(1)));

        Assert.That(spent.RateLimit!.Value.Limit, Is.EqualTo(100));
        Assert.That(spent.RateLimit.Value.Remaining, Is.EqualTo(0));
        Assert.That(busy.RateLimit, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ABackendFailureNamesWhoseApiItWas(bool timedOut)
    {
        using var response = From(timedOut
            ? GatewayRefusals.BackendTimedOut("geography")
            : GatewayRefusals.BackendUnreachable("geography"));

        var error = await response.ToDataProviderErrorAsync();

        Assert.That(error!.Type, Is.EqualTo(
            timedOut ? DataProviderErrorType.ProviderTimedOut : DataProviderErrorType.ProviderUnreachable));

        // A consumer seeing a bare 502 from api.nuvtools.com reasonably concludes the marketplace is
        // down. Naming the API is what lets them tell one provider's outage from ours.
        Assert.That(error.ApiCode, Is.EqualTo("geography"));
        Assert.That(error.Message, Does.Contain("geography"));
        Assert.That(error.IsTransient, Is.True);
    }

    [Test]
    public async Task AProvidersOwnNotFoundIsAnAnswerAndNotAPlatformRefusal()
    {
        using var routable = From(GatewayRefusals.ProvidersOwnNotFound());
        using var withdrawn = From(GatewayRefusals.NotRoutable());

        // The provider answered a question. Classifying this as "the API is gone" would send a
        // caller chasing a subscription problem that does not exist.
        Assert.That((await routable.ToDataProviderErrorAsync())!.Type, Is.EqualTo(DataProviderErrorType.Unknown));

        // The platform's own 404 carries no body at all.
        Assert.That((await withdrawn.ToDataProviderErrorAsync())!.Type, Is.EqualTo(DataProviderErrorType.ApiNotRoutable));
    }

    [Test]
    public async Task ABodyTooLargeIsReportedWithoutLeakingTheCeiling()
    {
        using var response = From(GatewayRefusals.RequestTooLarge());

        var error = await response.ToDataProviderErrorAsync();

        Assert.That(error!.Type, Is.EqualTo(DataProviderErrorType.RequestTooLarge));
        Assert.That(error.IsTransient, Is.False);
    }

    [Test]
    public async Task TheAllowanceIsReadableOnASuccessSoACallerCanSlowDownBeforeItRunsOut()
    {
        using var response = From(GatewayRefusals.Ok(limit: 100, remaining: 4));

        Assert.That(await response.ToDataProviderErrorAsync(), Is.Null);

        var allowance = response.RateLimit();

        Assert.That(allowance, Is.Not.Null);
        Assert.That(allowance.Value.Limit, Is.EqualTo(100));
        Assert.That(allowance.Value.Remaining, Is.EqualTo(4));
        Assert.That(allowance.Value.ResetsOnUtc > DateTimeOffset.UtcNow, Is.True);
    }

    [Test]
    public async Task AnAllowanceThePlatformDidNotPublishIsNeverGuessed()
    {
        // No ceiling, a test credential, or a counter the platform could not read: it says nothing
        // rather than reporting a figure it does not have.
        using var response = From(GatewayRefusals.Ok());

        Assert.That(response.RateLimit(), Is.Null);
        Assert.That(await response.ToDataProviderErrorAsync(), Is.Null);
    }

    [Test]
    public async Task EnsureSuccessThrowsTheTypeAndLeavesAProvidersOwnAnswerAlone()
    {
        using var refused = From(GatewayRefusals.CredentialRefused());

        var exception = await Assert.ThrowsAsync<DataProviderException>(() => refused.EnsureSuccessAsync());

        Assert.That(exception!.Type, Is.EqualTo(DataProviderErrorType.CredentialRefused));

        // A provider's own 404 is an answer; EnsureSuccessAsync deliberately does less than
        // EnsureSuccessStatusCode and lets it through.
        using var providerAnswer = From(GatewayRefusals.ProvidersOwnNotFound());

        await providerAnswer.EnsureSuccessAsync();
    }
}
