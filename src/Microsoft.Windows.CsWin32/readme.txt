Microsoft Windows SDK Win32 API Source Generator
==================================================

This package contains a source generator to add a user-defined set of Win32 P/Invoke
methods and supporting types to a C# project.

To get started, create a "NativeMethods.txt" file in your project directory
that lists the names of Win32 APIs for which you need to have generated, one per line.

Tips
----

Remove the `IncludeAssets` metadata from the package reference so that you get better code generation
by allowing nuget to bring in the `System.Memory` package as a transitive dependency.

```diff
 <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.1.647-beta">
   <PrivateAssets>all</PrivateAssets>
-  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
 </PackageReference>
```

Your project must allow unsafe code to support the generated code that will likely use pointers.

Learn more from our README on GitHub: https://github.com/microsoft/CsWin32#readme
