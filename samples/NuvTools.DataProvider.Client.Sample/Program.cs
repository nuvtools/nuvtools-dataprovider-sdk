using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuvTools.DataProvider.Client;

// Every setting is listed in appsettings.json. User secrets and the environment override it, which
// is where the token belongs: the file is in the repository, so its Token stays empty.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("DataProvider");

// dotnet run -- <api code> <path> calls something else without editing the file.
var apiCode = args.Length > 0 ? args[0] : settings["ApiCode"] ?? string.Empty;
var path = args.Length > 1 ? args[1] : settings["Path"] ?? string.Empty;
var token = settings["Token"];

if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("No access token. Store the one your subscription was issued with:");
    Console.Error.WriteLine("  dotnet user-secrets set DataProvider:Token <token>");
    Console.Error.WriteLine("or set the DataProvider__Token environment variable.");

    return 1;
}

var services = new ServiceCollection();

// One registration per API. It adds an HttpClient named after the API's code, based at the API's
// address, sending the token on every call and retrying only what is safe to retry.
services.AddNuvToolsDataProvider(options =>
{
    options.ApiCode = apiCode;

    // Asked for on every call, so a real application can read it from a vault and rotate it without
    // a restart. Here it is the value read above.
    options.TokenProvider = _ => ValueTask.FromResult(token);

    // The rest have defaults in the package; the file states them so they can be seen and changed.
    // BaseAddress is only ever changed to point at a test environment.
    options.BaseAddress = settings.GetValue("BaseAddress", options.BaseAddress)!;
    options.Timeout = TimeSpan.FromSeconds(settings.GetValue("TimeoutSeconds", options.Timeout.TotalSeconds));
    options.EnableRetries = settings.GetValue("EnableRetries", options.EnableRetries);
});

await using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(apiCode);

HttpResponseMessage response;

try
{
    // A relative path, resolved against https://api.nuvtools.com/<api code>/.
    response = await client.GetAsync(path);
}
catch (HttpRequestException exception)
{
    // Reached only after the retries are spent: the network, not an answer.
    Console.Error.WriteLine($"The call did not reach the platform: {exception.Message}");

    return 1;
}

using (response)
{
    Console.WriteLine($"GET {response.RequestMessage?.RequestUri}");
    Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
    Console.WriteLine($"Request id: {response.RequestId() ?? "(none)"}");

    // Sent on a success as well as on a refusal, so a caller can slow down before running out.
    // Absent when the subscription has no monthly ceiling.
    if (response.RateLimit() is { } allowance)
    {
        Console.WriteLine($"Allowance: {allowance.Remaining} of {allowance.Limit} calls left, renewing on {allowance.ResetsOnUtc:u}");
    }

    Console.WriteLine();

    if (await response.ToDataProviderErrorAsync() is { } error)
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine(Advice(error));

        // Not a platform refusal: the provider's own API wrote this answer, and its body is theirs.
        if (!error.IsPlatformRefusal)
        {
            Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
        }

        return 1;
    }

    // The package is not a typed client for any API: the body is whatever the provider documents.
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}

return 0;

// Branches on the type and never on the message, which is localized and written for a person.
static string Advice(DataProviderError error)
{
    return error.Type switch
    {
        DataProviderErrorType.CredentialRefused =>
            "The token was not accepted. Check it, and that the subscription it belongs to is active.",
        DataProviderErrorType.QuotaExceeded =>
            $"The monthly allowance is spent; it renews in {error.RetryAfter}. Retrying before then cannot succeed.",
        DataProviderErrorType.RateLimited =>
            $"Too many calls at once. The allowance is fine; wait {error.RetryAfter} and send it again.",
        DataProviderErrorType.PlatformUnavailable =>
            "The platform could not verify the token right now. The token is fine; send the call again unchanged.",
        DataProviderErrorType.ProviderUnreachable or DataProviderErrorType.ProviderTimedOut =>
            $"The provider of '{error.ApiCode}' is not answering. The outage is theirs.",
        DataProviderErrorType.ApiNotRoutable =>
            "No API is published under that code.",
        DataProviderErrorType.RequestTooLarge =>
            "The request body is larger than this API accepts.",
        _ =>
            "The provider's API answered this itself; see its documentation for what the status means."
    };
}
