<h1 align="center">C#/Win32 Interop Projection</h1>

<p align="center">
  <a style="text-decoration:none" href="https://www.nuget.org/packages/Microsoft.Windows.CsWin32">
    <img src="https://img.shields.io/nuget/v/Microsoft.Windows.CsWin32" alt="NuGet badge" /></a>
  <a style="text-decoration:none" href="https://dev.azure.com/azure-public/winsdk/_build/latest?definitionId=47&branchName=main">
    <img src="https://dev.azure.com/azure-public/winsdk/_apis/build/status/microsoft.CsWin32?branchName=main" alt="NuGet badge" /></a>
</p>

C#/Win32 provides **P/Invoke** and **COM Interop** projection support for C#. It generates strongly-typed, source-generated bindings from CsWn32-compatible `.winmd` metadata files. CsWin32 supports the metadata of `Microsoft.Windows.SDK.Win32Metadata` as the 1st-party metadata.

- Generates interop code quickly at compilation time.
- Generates friendly overloads/extensions (including `SafeHandle`-types support).
- Generates xml documentation based on and links back to learn.microsoft.com
- Ships no bulky assemblies alongside your application.

## Getting started

- [Getting started](https://microsoft.github.io/CsWin32/docs/getting-started.html)
- [Examples](https://microsoft.github.io/CsWin32/docs/examples.html)
- [3rd-party metadata support](https://microsoft.github.io/CsWin32/docs/3rdPartyMetadata.html)

## Demo

![Animation demonstrating p/invoke code generation](docfx/images/demo.gif)
