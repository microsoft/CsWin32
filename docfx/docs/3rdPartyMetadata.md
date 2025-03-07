# 3rd party metadata

CsWin32 comes with dependencies on Windows metadata for the SDK and WDK, allowing C# programs to generate interop code for Windows applications.
But the general transformation from metadata to C# code may be applied to other metadata inputs, allowing you to generate similar metadata for 3rd party native libraries and use CsWin32 to generate C# interop APIs for it.

## Constructing metadata for other libraries

Constructing metadata is outside the scope of this document.
However you may find [the win32metadata architecture](https://github.com/microsoft/win32metadata/blob/main/docs/architecture.md) document instructive.

## Hooking metadata into CsWin32

Metadata is fed into CsWin32 through MSBuild items.

Item Type | Purpose
--|--
`ProjectionMetadataWinmd` | Path to the .winmd file.
`ProjectionDocs` | Path to an optional msgpack data file that contains API-level documentation.
`AppLocalAllowedLibraries` | The filename (including extension) of a native library that is allowed to ship in the app directory (as opposed to only %windir%\system32).

## Packaging up metadata

Build a NuGet package with the following layout:

```
buildTransitive\
   YourPackageId.props
   yournativelib.winmd
runtimes\
   win-x86\
      yournativelib.dll
   win-x64\
      yournativelib.dll
   win-arm64\
      yournativelib.dll
   ...
```

Your package metadata may want to express a dependency on the Microsoft.Windows.CsWin32 package.

The `YourPackageId.props` file should include the msbuild items above, as appropriate.
For example:

```xml
<Project>
  <ItemGroup>
    <ProjectionMetadataWinmd Include="$(MSBuildThisFileDirectory)yournativelib.winmd" />
    <AppLocalAllowedLibraries Include="yournativelib.dll" />
  </ItemGroup>
</Project>
```

## Consuming your package

A project can reference your NuGet package to get both the native dll deployed with their app and the C# interop APIs generated as they require through NativeMethods.txt using CsWin32, just like they can for Win32 APIs.
