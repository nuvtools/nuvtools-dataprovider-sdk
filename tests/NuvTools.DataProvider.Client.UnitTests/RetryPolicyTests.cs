using Polly;
using System.Net;

namespace NuvTools.DataProvider.Client.UnitTests;

/// <summary>
/// What the client will and will not send again.
/// </summary>
/// <remarks>
/// The two claims worth testing are the ones nobody can see by reading a retry count: that a spent
/// monthly quota is never retried although it answers 429, and that a method which is not idempotent
/// is never retried although the status says it could be.
/// </remarks>
[TestFixture]
public class RetryPolicyTests
{
    private static Outcome<HttpResponseMessage> Outcome(HttpResponseMessage response, HttpMethod method)
    {
        response.RequestMessage = new HttpRequestMessage(method, new Uri("https://api.nuvtools.com/geography/v1/countries"));

        return Polly.Outcome.FromResult(response);
    }

    [Test]
    public void ASpentMonthlyQuotaIsNeverRetriedAlthoughItAnswers429()
    {
        using var quota = GatewayRefusals.QuotaExceeded();

        // Its Retry-After is three weeks. Retrying it sits in a loop until the month turns, or —
        // worse — a caller concludes the platform is broken and stops calling entirely.
        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(quota, HttpMethod.Get)), Is.False);
    }

    [Test]
    public void ABusySecondIsRetried()
    {
        using var limited = GatewayRefusals.RateLimited();

        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(limited, HttpMethod.Get)), Is.True);
    }

    [TestCase("GET", true)]
    [TestCase("HEAD", true)]
    [TestCase("PUT", true)]
    [TestCase("DELETE", true)]
    [TestCase("POST", false)]
    [TestCase("PATCH", false)]
    public void OnlyIdempotentMethodsAreSentAgain(string method, bool expected)
    {
        using var unreachable = GatewayRefusals.BackendUnreachable("geography");

        // A retried POST is a duplicate order in somebody else's system. PATCH is not idempotent by
        // definition either, however often it happens to be in practice.
        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(unreachable, new HttpMethod(method))),
            Is.EqualTo(expected));
    }

    [Test]
    public void ACredentialTheCallerHasToFixIsNeverRetried()
    {
        using var refused = GatewayRefusals.CredentialRefused();
        using var tooLarge = GatewayRefusals.RequestTooLarge();

        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(refused, HttpMethod.Get)), Is.False);
        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(tooLarge, HttpMethod.Get)), Is.False);
    }

    [Test]
    public void OurOwnOutageIsRetriedUnchanged()
    {
        using var unavailable = GatewayRefusals.ValidationUnavailable();

        // 503 means the platform could not verify the credential, not that the credential is wrong.
        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(unavailable, HttpMethod.Get)), Is.True);
    }

    [Test]
    public void ASuccessAndATransportFailureAreHandledTheObviousWay()
    {
        using var ok = GatewayRefusals.Ok();

        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(ok, HttpMethod.Get)), Is.False);

        Assert.That(ServiceCollectionExtensions.ShouldRetry(
            Polly.Outcome.FromException<HttpResponseMessage>(new HttpRequestException("socket"))), Is.True);
    }

    [Test]
    public void AProvidersOwnAnswerIsNotRetried()
    {
        using var providerNotFound = GatewayRefusals.ProvidersOwnNotFound();

        // The provider answered. Retrying would ask the same question and be billed for it twice.
        Assert.That(ServiceCollectionExtensions.ShouldRetry(Outcome(providerNotFound, HttpMethod.Get)), Is.False);
    }
}
