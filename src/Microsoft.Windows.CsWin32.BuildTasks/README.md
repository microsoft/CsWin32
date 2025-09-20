# CsWin32 MSBuild Task

This project contains an MSBuild task that invokes CsWin32 code generation at build time, similar to how the Roslyn source generator works but as a build task instead.

## Architecture

The solution consists of two main components:

1. **CsWin32Generator** (`src/CsWin32Generator/`) - A command line tool that performs the actual code generation
2. **CsWin32CodeGeneratorTask** (`src/Microsoft.Windows.CsWin32.BuildTasks/`) - An MSBuild ToolTask that invokes the command line tool

## Usage

### In your project file:

```xml
<PropertyGroup>
  <CsWin32GenerateAtBuild>true</CsWin32GenerateAtBuild>
  <CsWin32OutputPath>$(IntermediateOutputPath)Generated\</CsWin32OutputPath>
</PropertyGroup>

<ItemGroup>
  <CsWin32NativeMethodsTxt Include="NativeMethods.txt" />
  <CsWin32NativeMethodsJson Include="NativeMethods.json" Condition="Exists('NativeMethods.json')" />
  <CsWin32MetadataPath Include="$(PkgMicrosoft_Windows_SDK_Win32Metadata)\content\Windows.Win32.winmd" />
</ItemGroup>

<Import Project="path/to/CsWin32.targets" />
```

### Required files:

- **NativeMethods.txt** - Contains the list of APIs to generate, one per line
- **NativeMethods.json** (optional) - Contains generator options in JSON format

### Example NativeMethods.txt:
```
GetCurrentProcess
GetProcessId
SetWindowText
MessageBoxW
```

### Example NativeMethods.json:
```json
{
  "allowMarshaling": true,
  "className": "NativeMethods",
  "public": false
}
```

## Command Line Tool Usage

The CsWin32Generator tool can also be used directly:

```bash
CsWin32Generator.exe \
  --native-methods-txt NativeMethods.txt \
  --metadata-paths Windows.Win32.winmd \
  --output-path Generated/ \
  --allow-unsafe-blocks true \
  --target-framework net8.0 \
  --platform AnyCPU
```

## Input Properties

The MSBuild task accepts the same input properties as the CsWin32 source generator:

- **NativeMethodsTxt** (required) - Path to NativeMethods.txt file
- **NativeMethodsJson** (optional) - Path to NativeMethods.json file  
- **MetadataPaths** (required) - Semicolon-separated paths to .winmd metadata files
- **DocPaths** (optional) - Semicolon-separated paths to documentation files
- **AppLocalAllowedLibraries** (optional) - Semicolon-separated paths to app-local libraries
- **OutputPath** (required) - Directory where generated files will be written
- **AllowUnsafeBlocks** - Whether unsafe code is allowed (default: true)
- **TargetFramework** - Target framework version (affects available features)
- **Platform** - Target platform (x86, x64, AnyCPU, etc.)
- **References** - Additional assembly references for compilation context

## Output

The task generates .g.cs files in the specified output directory and returns them as the **GeneratedFiles** output property, which are automatically added to the project's Compile items.