# C#/Win32 P/Invoke Source Generator

***A source generator to add a user-defined set of Win32 P/Invoke methods and supporting types to a C# project.***

[![NuGet (prerelease)](https://img.shields.io/nuget/v/Microsoft.Windows.CsWin32)](https://www.nuget.org/packages/Microsoft.Windows.CsWin32)
[![NuGet (daily)](https://img.shields.io/badge/nuget-daily-red)](https://dev.azure.com/azure-public/winsdk/_packaging?_a=package&feed=CI%40Local&package=Microsoft.Windows.CsWin32&protocolType=NuGet)

[![Build Status](https://dev.azure.com/azure-public/winsdk/_apis/build/status/microsoft.CsWin32?branchName=main)](https://dev.azure.com/azure-public/winsdk/_build/latest?definitionId=47&branchName=main)

## Features

* Rapidly add P/Invoke methods and supporting types to your C# project.
* No bulky assemblies to ship alongside your application.
* `SafeHandle`-types automatically generated.
* Generates xml documentation based on and links back to docs.microsoft.com

![Animation demonstrating p/invoke code generation](docfx/images/demo.gif)

## Usage

[Check out our product documentation](https://microsoft.github.io/CsWin32/).
