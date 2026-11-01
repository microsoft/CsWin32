# Public Agility SDK test metadata

`AgilitySdk.proj` generates the checked-in test WinMD from the `d3d12.h`
header in the publicly released `Microsoft.Direct3D.D3D12` **1.619.3** package.
It uses public Windows CPP **10.0.26100.7705** headers and Windows metadata
**71.0.25-preview** for dependencies. No private Agility headers or PR-build
SDK payloads are included.

The project uses the published WinmdGenerator 0.71.25-preview SDK with the
corrected scraper and emitter tools from public
[`microsoft/win32metadata` commit a06c33ef3b5c97b4f74ad192bb183b1768094d4e](https://github.com/microsoft/win32metadata/tree/a06c33ef3b5c97b4f74ad192bb183b1768094d4e).
The uncorrected emitter duplicates inherited COM slots when definitions overlap
its Windows reference, so it must not be used to regenerate this fixture.

Build `BuildTools` in a separate checkout of that immutable public commit using
`dotnet build BuildTools -c Release`, then run from the CsWin32 repository:

```powershell
$env:NBGV_GitEngine = 'Disabled'
dotnet restore test\AgilitySdk\AgilitySdk.proj
dotnet msbuild test\AgilitySdk\AgilitySdk.proj -t:Build `
  -p:CorrectedEmitterToolsDir=<absolute-path-to-built-net10.0-tools>
```

The generated output is
`test\Microsoft.Windows.CsWin32.Tests\ExternalMetadata\Microsoft.Direct3D.D3D12.winmd`.
This is a generated public-header test fixture, not an official SDK WinMD package.
CI consumes the checked-in fixture without running the scraper.
The fixture SHA256 is
`62949F20A9D72674ECE71B45B6AF76A607970AE55FFC9EEF9F02346907C2D750`.

Tests restore the complete public Windows metadata **70.0.11-preview** baseline
separately. It retains the local D3D12 definitions and lacks
`D3D12_FEATURE_D3D12_OPTIONS22`, which the supplemental public SDK adds.
