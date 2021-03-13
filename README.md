# C#/Win32 P/Invoke Source Generator

***A source generator to add a user-defined set of Win32 P/Invoke methods and supporting types to a C# project.***

[![NuGet (prerelease)](https://img.shields.io/nuget/vpre/Microsoft.Windows.CsWin32)](https://www.nuget.org/packages/Microsoft.Windows.CsWin32)
[![NuGet (daily)](https://img.shields.io/badge/nuget-daily-red)](https://dev.azure.com/azure-public/winsdk/_packaging?_a=package&feed=CI%40Local&package=Microsoft.Windows.CsWin32&protocolType=NuGet)

[![Build Status](https://dev.azure.com/azure-public/winsdk/_apis/build/status/microsoft.CsWin32?branchName=main)](https://dev.azure.com/azure-public/winsdk/_build/latest?definitionId=47&branchName=main)

## Features

* Rapidly add P/Invoke methods and supporting types to your C# project.
* No bulky assemblies to ship alongside your application.
* `SafeHandle`-types automatically generated.
* Generates xml documentation based on and links back to docs.microsoft.com

![Animation demonstrating p/invoke code generation](doc/demo.gif)

## Prerequisites

The .NET 5 SDK or Visual Studio 2019 Update 8 (16.8) for the C# compiler that added support for Source Generators.
The experience with source generators in Visual Studio is still improving, and is noticeably better in VS 16.9. WPF projects have [additional requirements](https://github.com/microsoft/CsWin32/issues/7).

In addition, some generated code may require use of the C# 9 language version (`<LangVersion>9</LangVersion>`) in your project file. See [issue #4](https://github.com/microsoft/CsWin32/issues/4) for more on this.

See [dotnet/pinvoke](https://github.com/dotnet/pinvoke) for precompiled NuGet packages with Win32 P/Invokes.

## Usage

Install the `Microsoft.Windows.CsWin32` package:

```ps1
dotnet add package Microsoft.Windows.CsWin32 -pre
```

Your project must allow unsafe code to support the generated code that will likely use pointers.
This does *not* automatically make all your code *unsafe*.
Use of the `unsafe` keyword is required anywhere you use pointers.
The source generator NuGet package sets the default value of the `AllowUnsafeBlocks` property for your project to `true`,
but if you explicitly set it to `false` in your project file, generated code may produce compiler errors.

Create a `NativeMethods.txt` file in your project directory that lists the APIs to generate code for.
Each line may consist of *one* of the following:

* Exported method name (e.g. `CreateFile`). This *may* include the `A` or `W` suffix, where applicable.
* Module name followed by `.*` to generate all methods exported from that module (e.g. `Kernel32.*`).
* The name of a struct, enum, constant or interface to generate.
* A comment (i.e. any line starting with `//`) or white space line, which will be ignored.

When generating any type or member, all supporting types will also be generated.

Generated code is added directly in the compiler.
An IDE may make this generated code available to view through code navigation commands (e.g. Go to Definition) or a tree view of source files that include generated source files.

Assuming default settings and a `NativeMethods.txt` file with content that includes `CreateFile`, the P/Invoke methods can be found on the `Microsoft.Windows.Sdk.PInvoke` class, like this:

```cs
using Microsoft.Windows.Sdk;

PInvoke.CreateFile(/*args*/);
```

Constants are defined on the `Microsoft.Windows.Sdk.Constants` class.

Other supporting types are defined within the `Microsoft.Windows.Sdk` namespace.

### Customizing generated code

Several aspects of the generated code can be customized, including:

* The name of the class(es) that declare p/invoke methods
* The namespace that declares all interop types
* Whether to emit interop types as `public` or `internal`
* Whether to emit ANSI functions as well where Wide character functions also exist

To configure these settings, create a `NativeMethods.json` file in your project directory.
Specifying the `$schema` property adds completions, descriptions and validation in many JSON editors.

```json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "emitSingleFile": false
}
```

### Newer metadata

To update the metadata used as the source for code generation, you may install a newer `Microsoft.Windows.SDK.Win32Metadata` package:

```ps1
dotnet add package Microsoft.Windows.SDK.Win32Metadata -pre
```

Alternatively, you may set the `MicrosoftWindowsSdkWin32MetadataBasePath` property in your project file to the path of the directory containing `Windows.Win32.winmd`:

```xml
<MicrosoftWindowsSdkWin32MetadataBasePath>c:\path\to\dir</MicrosoftWindowsSdkWin32MetadataBasePath>
```

## Known issues

- [**WPF projects** issue and workaround](https://github.com/microsoft/CsWin32/issues/7).

## Consuming daily builds

Can't wait for the next release to try out a bug fix? Follow these steps to consume directly from our daily build.

Just add this package feed to your nuget.config file:

   ```xml
   <add key="winsdk" value="https://pkgs.dev.azure.com/azure-public/winsdk/_packaging/CI/nuget/v3/index.json" />
   ```
