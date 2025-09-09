# CustomIInspectable

This was built following https://withinrafael.com/2023/01/18/generating-metadata-for-the-windows-crate

## To build

```
dotnet restore
dotnet msbuild
```

It's important to use `dotnet msbuild` because WinMDGenerator is net8-targeting msbuild tasks which can only function inside dotnet msbuild and do not work in "dotnet build" or "msbuild".
