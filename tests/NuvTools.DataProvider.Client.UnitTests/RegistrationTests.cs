using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace NuvTools.DataProvider.Client.UnitTests;

/// <summary>
/// Registering a client, and what the token handler does with a request.
/// </summary>
[TestFixture]
public class RegistrationTests
{
    private static DataProviderOptions Options(string apiCode = "geography", string token = "ntp_live_abc") => new()
    {
        ApiCode = apiCode,
        TokenProvider = _ => ValueTask.FromResult(token)
    };

    [Test]
    public void TheClientIsNamedAfterTheApiAndItsAddressEndsWhereRelativePathsExpect()
    {
        var services = new ServiceCollection();

        services.AddNuvToolsDataProvider(options =>
        {
            options.ApiCode = "geography";
            options.TokenProvider = _ => ValueTask.FromResult("ntp_live_abc");
        });

        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("geography");

        // The trailing slash is load-bearing: without it a relative "v1/countries" would replace the
        // API's code instead of hanging off it.
        Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://api.nuvtools.com/geography/")));
        Assert.That(new Uri(client.BaseAddress!, "v1/countries"),
            Is.EqualTo(new Uri("https://api.nuvtools.com/geography/v1/countries")));
    }

    [Test]
    public void AnIncompleteRegistrationIsRefusedAtStartupRatherThanAtTheFirstCall()
    {
        var services = new ServiceCollection();

        // No token provider: a registration that looks fine until the first call is made at 3am.
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNuvToolsDataProvider(options => options.ApiCode = "geography"));

        Assert.Throws<InvalidOperationException>(() =>
            services.AddNuvToolsDataProvider(options =>
                options.TokenProvider = _ => ValueTask.FromResult("ntp_live_abc")));
    }

    [Test]
    public async Task TheTokenIsFetchedPerCallSoRotatingItNeedsNoRestart()
    {
        var issued = 0;

        var options = new DataProviderOptions
        {
            ApiCode = "geography",
            TokenProvider = _ => ValueTask.FromResult($"ntp_live_{++issued}")
        };

        var recorder = new RecordingHandler();

        using var client = new HttpClient(new DataProviderTokenHandler(options) { InnerHandler = recorder })
        {
            BaseAddress = options.ApiBaseAddress
        };

        await client.GetAsync("v1/countries");
        await client.GetAsync("v1/countries");

        Assert.That(recorder.Authorizations, Is.EqualTo(new[] { "Bearer ntp_live_1", "Bearer ntp_live_2" }));
    }

    [Test]
    public async Task ATokenTheCallerSetThemselvesIsLeftAlone()
    {
        var recorder = new RecordingHandler();

        using var client = new HttpClient(new DataProviderTokenHandler(Options()) { InnerHandler = recorder })
        {
            BaseAddress = new Uri("https://api.nuvtools.com/geography/")
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/countries");

        // Most often a test credential for one call. Replacing it silently would cost an afternoon.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ntp_test_mine");

        await client.SendAsync(request);

        Assert.That(recorder.Authorizations, Is.EqualTo(new[] { "Bearer ntp_test_mine" }));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString() ?? "(none)");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
