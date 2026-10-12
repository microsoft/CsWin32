# Contributing

This project has adopted the [Microsoft Open Source Code of
Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct
FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com)
with any additional questions or comments.

## Building and testing

Install the [.NET SDK](https://get.dot.net/) specified in `global.json` (or a newer patch in the same SDK feature band). On Windows, open `Microsoft.Windows.CsWin32.slnx` in a Visual Studio version that supports that SDK and C# 14, then build the solution. No initialization script, CLI tool restore, or custom shell environment is required.

From the repository root, the equivalent commands are:

```console
dotnet build
dotnet test --filter "TestCategory!=HighMemory&TestCategory!=RequiresHardware"
dotnet pack -c Release
```

Package restore runs automatically. Dependencies come from nuget.org, except for the source-generator testing previews available only on Microsoft's public `msft_consumption` feed; `nuget.config` maps those packages explicitly. `dotnet pack` builds the solution before packaging, including the non-packable generator and MSBuild task whose outputs are shipped in the package. Packages are written to `bin/Packages/<Configuration>/NuGet`.

Tests target .NET 8, 9, 10, and .NET Framework 4.7.2. Install the corresponding runtimes to run the full matrix; the .NET 10 SDK alone is sufficient to compile it. Windows-specific tests need Windows; .NET Framework tests need .NET Framework on Windows or Mono on Linux/macOS. To build on Linux/macOS, use `dotnet build -p:Platform=NonWindows` and the same platform property when testing or packing.

The excluded tests exercise large metadata sets or Windows runtime services. They remain covered by the separate CI jobs and can also be run locally by changing the filter.

## Official builds

Microsoft's Azure Pipelines retain the 1ESPT/MicroBuild toolset, signing, SBOM generation, artifact collection, and release/insertion pipelines. `EnableMicroBuild` defaults to true when `TF_BUILD=true`, and those builds restore from `azure-pipelines/nuget.config`, preserving the `msft_consumption` feed. Local and GitHub Actions builds do not load the MicroBuild packages or SBOM configuration.

`init.ps1` remains an optional environment installer and the official pipelines' bootstrap. Use [PowerShell][pwsh] to run it. For a local build that intentionally uses MicroBuild, run `init.ps1 -Signing` or pass `-p:EnableMicroBuild=true` when restoring and building; authenticated feed access and the signing plugins may be required. CLI tools for documentation, coverage, release versioning, and source archival can be restored separately with `dotnet tool restore`. These tools are not needed for an ordinary solution build.

## Parsing the metadata

The win32metadata has [these little "gems"](https://github.com/microsoft/win32metadata/blob/main/docs/projections.md) that CsWin32 should consider consuming to improve the quality of the generated APIs.
When we identify a gem that we should support but do not yet support, file [an issue with a `metadata gem` label](https://github.com/microsoft/CsWin32/issues?q=is%3Aissue+is%3Aopen+label%3A%22metadata+gem%22).

## Updating the win32metadata version

To consume a newer [win32metadata](https://github.com/microsoft/win32metadata) release, update the `MetadataVersion` (and, when a new WDK metadata package ships, `WDKMetadataVersion`) property in `Directory.Packages.props`.

Local and GitHub Actions builds restore these packages directly from nuget.org. Official builds use the `msft_consumption` feed (`azure-public/vside`), which pulls them from nuget.org via an upstream source. A new package version is **not** available on that feed until it has been *pulled through* at least once, and only a feed **Collaborator/Contributor** can trigger that.

Azure Artifacts can take [3-6 hours to discover a newly published nuget.org package](https://learn.microsoft.com/azure/devops/artifacts/concepts/upstream-sources#upstream-sources-health-status).
If the pull-through pipeline also fails with `NU1102` despite the package being available on nuget.org, wait for upstream discovery and queue it again before rerunning the PR checks.

To pull the new version through:

1. Push your branch (with the bumped `MetadataVersion`) to GitHub.
2. Queue the **CsWin32 unofficial** Azure Pipeline (`devdiv/DevDiv`, definition `25100`, YAML `azure-pipelines/unofficial.yml`) against your branch. Its build agent runs as a feed Collaborator, so its restore pulls the new package onto the feed automatically.
3. Wait until the pipeline reaches its `dotnet build` step, then confirm the version is on the feed (it is then available to official builds):

   ```pwsh
   $idx = Invoke-RestMethod "https://pkgs.dev.azure.com/azure-public/vside/_packaging/msft_consumption/nuget/v3/flat2/microsoft.windows.sdk.win32metadata/index.json"
   $idx.versions | Where-Object { $_ -like '<new-version>*' }
   ```

After the package is on the feed, build and run the tests (`dotnet test --filter "TestCategory!=RequiresHardware"`) and fix any regressions before opening the PR.

[pwsh]: https://learn.microsoft.com/powershell/scripting/install/installing-powershell

## Releases

Use `nbgv tag` to create a tag for a particular commit that you mean to release.
[Learn more about `nbgv` and its `tag` and `prepare-release` commands](https://dotnet.github.io/Nerdbank.GitVersioning/docs/nbgv-cli.html).

Push the tag.

### Azure Pipelines

When your repo builds with Azure Pipelines, use the `azure-pipelines/release.yml` pipeline.
Trigger the pipeline by adding the `auto-release` tag on a run of your main `azure-pipelines.yml` pipeline.

## Tutorial and API documentation

API and hand-written docs are found under the `docfx/` directory and are built by [docfx](https://dotnet.github.io/docfx/).

You can make changes and host the site locally to preview them by switching to that directory and running the `dotnet docfx --serve` command.
After making a change, you can rebuild the docs site while the localhost server is running by running `dotnet docfx` again from a separate terminal.

The `.github/workflows/docs.yml` GitHub Actions workflow publishes the content of these docs to github.io if the workflow itself and [GitHub Pages is enabled for your repository](https://docs.github.com/en/pages/quickstart).

## Updating dependencies

This repo uses Renovate to keep dependencies current.
Configuration is in the `.github/renovate.json` file.
[Learn more about configuring Renovate](https://docs.renovatebot.com/configuration-options/).

When changing the renovate.json file, follow [these validation steps](https://docs.renovatebot.com/config-validation/).

The optional `init.ps1 -RestoreRetryCount 10` retries restores with 60 seconds between attempts only when Azure Artifacts reports `No local versions of package` for an upstream package that has not been saved to the feed. Generic download and authentication failures fail immediately. GitHub Actions use nuget.org directly and do not need this feed-specific retry mechanism.

Run `./test/Restore.Tests.ps1` with PowerShell to exercise the retry behavior without restoring packages or installing test dependencies. These regression checks also run in the Windows and Linux build jobs.

If Renovate is not creating pull requests when you expect it to, check that the [Renovate GitHub App](https://github.com/apps/renovate) is configured for your account or repo.
