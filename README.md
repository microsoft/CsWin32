# C#/Win32 P/Invoke Source Generator

***A source generator to add a user-defined set of Win32 p/invoke methods and supporting types to a C# project.***

[![Build Status](https://dev.azure.com/devdiv/Personal/_apis/build/status/microsoft.CsWin32?branchName=main)](https://dev.azure.com/devdiv/Personal/_build/latest?definitionId=13899&branchName=main)

## Features

* Rapidly add p/invoke methods and supporting types to your C# project.
* No bulky assemblies to ship alongside your application.
* `SafeHandle`-types automatically generated.
* Generates xml documentation based on and links back to docs.microsoft.com

![Animation demonstrating p/invoke code generation](doc/demo.gif)

Source generator requires C# 9.
See [dotnet/pinvoke](https://github.com/dotnet/pinvoke) for precompiled NuGet packages with Win32 p/invokes.

## Usage

Add these feeds as package sources:

```xml
<add key="CsWin32" value="https://pkgs.dev.azure.com/devdiv/Personal/_packaging/CsWin32/nuget/v3/index.json" />
<add key="MSFTNuget" value="https://microsoft.pkgs.visualstudio.com/_packaging/MSFTNuget/nuget/v3/index.json" />
```

Install the `Microsoft.Windows.Sdk.PInvoke.CSharp` package:

```ps1
dotnet add package Microsoft.Windows.Sdk.PInvoke.CSharp -pre
```

Create a `NativeMethods.txt` file in your project directory and add it as an `AdditionalFile` item in your project:

```xml
<ItemGroup>
  <AdditionalFiles Include="NativeMethods.txt" />
</ItemGroup>
```

In your `NativeMethods.txt` file, list the APIs to generate code for.
Each line may consist of *one* of the following:

* Exported method name (e.g. `CreateFile`). This *may* include the `A` or `W` suffix, where applicable.
* Module name followed by `.*` to generate all methods exported from that module (e.g. `Kernel32.*`)
* The name of a struct, enum, or interface to generate.

When generating any type or member, all supporting types will also be generated.

Generated code is added directly in the compiler.
An IDE may make this generated code available to view through code navigation commands (e.g. Go to Definition) or a tree view of source files that include generated source files.

Assuming default settings and a `NativeMethods.txt` file with content that includes `CreateFile`, the p/invoke API can be found on the `Microsoft.Windows.Sdk.PInvoke` class, like this:

```cs
using Microsoft.Windows.Sdk;

PInvoke.CreateFile(/*args*/);
```

### Customizing generated code

Several aspects of the generated code can be customized, including:

* The name of the class(es) that declare p/invoke methods
* The namespace that declares all interop types
* Whether to emit interop types as `public` or `internal`
* Whether to emit ANSI functions as well where Wide character functions also exist

To configure these settings, create a `NativeMethods.json` file in your project directory and add it as an `AdditionalFile` item in your project:

```xml
<ItemGroup>
  <AdditionalFiles Include="NativeMethods.json" />
</ItemGroup>
```

The [`settings.schema.json`](src/Microsoft.Windows.Sdk.PInvoke.CSharp/settings.schema.json) file in this repo defines the JSON schema to use as content for this file.
Here is an example:

```json
{
  "$schema": "..\\..\\src\\Microsoft.Windows.Sdk.PInvoke.CSharp\\settings.schema.json",
  "emitSingleFile": true
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
