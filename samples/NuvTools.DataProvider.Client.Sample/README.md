# NuvTools.DataProvider.Client.Sample

A console application that calls an API through
[`NuvTools.DataProvider.Client`](../../src/NuvTools.DataProvider.Client/README.md), installed from
nuget.org the way any consumer installs it.

It makes one `GET`, then prints the status, the request id, the monthly allowance when the platform
reports one, and either the body or what the refusal means.

## Running it

You need a subscription to the API and the access token it was issued with.

```bash
cd samples/NuvTools.DataProvider.Client.Sample

dotnet user-secrets set DataProvider:Token <token>
dotnet run
```

With no arguments it calls `geography` at `v1/countries/BR`. Pass an API code and a path to call
something else:

```bash
dotnet run -- geography v1/brazil/states
```

## Settings

Read from user secrets, then from environment variables.

| Setting | Environment variable | |
|---|---|---|
| `DataProvider:Token` | `DataProvider__Token` | Required. The access token of your subscription. |
| `DataProvider:BaseAddress` | `DataProvider__BaseAddress` | Optional. Replaces `https://api.nuvtools.com/` to point at a test environment. |

## What to read in `Program.cs`

- **`AddNuvToolsDataProvider`** registers an `HttpClient` named after the API's code. No host and no
  ASP.NET Core are involved — a `ServiceCollection` is enough.
- **`response.RateLimit()`** is read on a successful answer too.
- **`ToDataProviderErrorAsync`** returns `null` on success, and the `Advice` function shows the one
  decision worth copying: branch on `error.Type`, where a spent monthly quota and a per-second rate
  limit are different cases even though both arrive as `429`.
