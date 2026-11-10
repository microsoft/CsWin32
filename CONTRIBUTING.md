# Contribution to CsWin32

You can contribute to CsWin32 with issues and PRs. Simply filing issues for problems you encounter is a great way to contribute. Contributing implementations is greatly appreciated.

## Prerequisites

- Visual Studio 2022 or 2026
- [.NET SDK](https://get.dot.net/) version specified in `global.json`
- Windows, Linux, or OSX.

## Building and Testing

Open `Microsoft.Windows.CsWin32.slnx` in Visual Studio to build the repository as a solution, or run `dotnet build` and `dotnet test` (the full test suite)

Package restore runs automatically. The test projects may need their target runtimes installed to run. Use `init.ps1` from PowerShell if you want the repository helper to install SDKs, runtimes, or the NuGet credential provider. It may set environment variables, so build from the same PowerShell session; use `-InstallLocality Machine` for machine-wide installs. The helper is optional when those prerequisites are already available.

## Parsing the metadata

The win32metadata has [these little "gems"](https://github.com/microsoft/win32metadata/blob/main/docs/projections.md) that CsWin32 should consider consuming to improve the quality of the generated APIs.
When we identify a gem that we should support but do not yet support, file [an issue with a `metadata gem` label](https://github.com/microsoft/CsWin32/issues?q=is%3Aissue+is%3Aopen+label%3A%22metadata+gem%22).

## Updating the win32metadata version

To consume a newer [win32metadata](https://github.com/microsoft/win32metadata) release, update the `MetadataVersion` (and, when a new WDK metadata package ships, `WDKMetadataVersion`) property in `Directory.Packages.props`.

CsWin32 restores these packages from the `msft_consumption` feed (`azure-public/vside`), which pulls them from nuget.org via an upstream source. A new package version is **not** available on the feed until it has been *pulled through* at least once, and only a feed **Collaborator/Contributor** can trigger that. Regular contributors typically only have **Reader** access, so a plain `dotnet restore` of the new version fails with `NU1102` until the package is on the feed.

Azure Artifacts can take [3-6 hours to discover a newly published nuget.org package](https://learn.microsoft.com/azure/devops/artifacts/concepts/upstream-sources#upstream-sources-health-status).
If the pull-through pipeline also fails with `NU1102` despite the package being available on nuget.org, wait for upstream discovery and queue it again before rerunning the PR checks.

To pull the new version through:

1. Push your branch (with the bumped `MetadataVersion`) to GitHub.
2. Queue the **CsWin32 unofficial** Azure Pipeline (`devdiv/DevDiv`, definition `25100`, YAML `azure-pipelines/unofficial.yml`) against your branch. Its build agent runs as a feed Collaborator, so its restore pulls the new package onto the feed automatically.
3. Wait until the pipeline reaches its `dotnet build` step, then confirm the version is on the feed (it is then restorable by everyone, including the GitHub Actions PR build):

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

### GitHub Actions

When your repo is hosted by GitHub and you are using GitHub Actions, you should create a GitHub Release using the standard GitHub UI.
Having previously used `nbgv tag` and pushing the tag will help you identify the precise commit and name to use for this release.

After publishing the release, the `.github/workflows/release.yml` workflow will be automatically triggered, which will:

1. Find the most recent `.github/workflows/build.yml` GitHub workflow run of the tagged release.
1. Upload the `deployables` artifact from that workflow run to your GitHub Release.
1. If you have `NUGET_API_KEY` defined as a secret variable for your repo or org, any nuget packages in the `deployables` artifact will be pushed to nuget.org.

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

Renovate PR validation retries package and tool restores up to 10 times, with 60 seconds between attempts, only when Azure Artifacts reports `No local versions of package` for an upstream package that has not been saved to the feed. Generic tool-download failures, authentication errors without that cache-miss message, and other restore errors fail immediately. When retries are enabled, tool restore uses detailed output so the underlying feed error is available for classification. This retry window does not replace the authenticated pull-through pipeline or the upstream discovery wait described above.

Run `./test/Restore.Tests.ps1` with PowerShell to exercise the retry behavior without restoring packages or installing test dependencies. These regression checks also run in the Windows and Linux build jobs.

If Renovate is not creating pull requests when you expect it to, check that the [Renovate GitHub App](https://github.com/apps/renovate) is configured for your account or repo.

## Merging latest from Library.Template

### Maintaining your repo based on this template

The best way to keep your repo in sync with Library.Template's evolving features and best practices is to periodically merge the template into your repo:

```ps1
git fetch
git checkout origin/main
./tools/MergeFrom-Template.ps1
# resolve any conflicts, then commit the merge commit.
git push origin -u HEAD
```
