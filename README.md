# nuvtools-dataprovider-sdk

The .NET libraries for the [Nuv Tools Data Provider](https://dataprovider.nuvtools.com) marketplace —
what somebody outside the platform installs to work with it.

| Package | For | |
|---|---|---|
| [`NuvTools.DataProvider.Client`](src/NuvTools.DataProvider.Client/README.md) | Consumers: calling an API you have subscribed to | `dotnet add package NuvTools.DataProvider.Client` |

```bash
dotnet build NuvTools.DataProvider.Sdk.slnx
dotnet test  NuvTools.DataProvider.Sdk.slnx
dotnet pack  src/NuvTools.DataProvider.Client/NuvTools.DataProvider.Client.csproj -c Release -o artifacts
```

## Layout

One solution, one folder per package, each with its own README, its own `<Version>` and its own
release tag.

```
NuvTools.DataProvider.Sdk.slnx
src/<Package>/                 the library, and the README that ships inside the package
tests/<Package>.UnitTests/     its tests
LICENSE, icon.png              shared by every package
```

## Rules for a package in this repository

- **It depends on nothing private.** A package here is installed into other people's processes, so
  it references only what is on nuget.org. It restates what it needs of the platform's wire
  contract — header names, the error envelope — rather than sharing code with the platform.
- **Its dependencies are kept at their current releases.** That means an application on an older
  target framework takes the newer `Microsoft.Extensions.*` packages along with this one; the
  trade is accepted in exchange for shipping current fixes.
- **It meters nothing.** Calls are counted in the platform's data plane and nowhere else; a library
  that counted them would be a second answer to what a call cost.

## Releasing

Publishing to nuget.org is irreversible per version, so the workflow makes it the last and smallest
step.

**One workflow per library.** Each package has its own — `client-publish.yml` for
`NuvTools.DataProvider.Client` — so a run and an approval each concern exactly one package. The
steps themselves are shared, in the reusable `package-publish.yml`.

1. Set the package's `<Version>` in its `.csproj` and merge it. That is the version that gets
   published; the workflow reads it and never computes one.
2. Run the library's workflow by hand (*Actions → Client Publish → Run workflow*) with *push*
   checked.
3. The workflow builds the solution with `-warnaserror`, runs the tests, packs **that package only**,
   verifies it carries its README, licence, icon, symbols and every target framework, and then waits
   for approval on the `nuget` GitHub Environment before pushing.
4. **After the push succeeds, the workflow tags the commit** `<package>-v<version>` —
   `client-v10.0.0` for `NuvTools.DataProvider.Client`. The tag is a record of what is on nuget.org,
   never what starts a release, so it cannot exist for a version that did not get there. A run
   whose tag already exists stops before building: the version has been released and `<Version>`
   needs a bump.

**No API key is stored.** The push uses nuget.org's trusted publishing: the job presents a GitHub
OIDC token, nuget.org checks it against the policy registered for this repository and answers with a
key that lives for one hour. The repository needs one secret, `NUGET_USER` — the nuget.org profile
name the policy belongs to — and the policy itself, registered under *Trusted Publishing* on
nuget.org with the owner `nuvtools`, the repository `nuvtools-dataprovider-sdk`, the workflow file
and the environment `nuget`.

A version that is already on nuget.org is skipped, not replaced: the push uses `--skip-duplicate`,
so re-running a release is harmless and publishing a change always needs a new `<Version>`.

Run a library's workflow by hand with *push* unchecked for a dry run that stops after the
verification.

To add a library: copy `client-publish.yml`, and change the name, the tag prefix, the project path
and the package id.

## License

MIT. See [LICENSE](LICENSE).
