// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpSourceGeneratorVerifier;

public class SourceGeneratorTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UnparseableNativeMethodsJson()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "CreateFile",
#pragma warning disable JSON001 // Invalid JSON pattern -- deliberate point of the test
            NativeMethodsJson = @"{ ""allowMarshaling"": f }",
#pragma warning restore JSON001 // Invalid JSON pattern
            TestState =
            {
                GeneratedSources =
                {
                    // Nothing generated, but no exceptions thrown that would lead Roslyn to disable the source generator in the IDE either.
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(SourceGenerator.OptionsParsingError.Id, DiagnosticSeverity.Error),
                },
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that no warning is produced even without the required reference, when no source is being generated anyway.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_NoGeneratedCode()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default.WithNuGetConfigFilePath(MyReferenceAssemblies.NuGetConfigPath),
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that a warning is produced when targeting a framework that our generated code requires the System.Memory reference for, but the reference is missing.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_NetFx472()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default.WithNuGetConfigFilePath(MyReferenceAssemblies.NuGetConfigPath),
            NativeMethodsTxt = "CreateFile",
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.MissingRecommendedReference.Id, DiagnosticSeverity.Warning),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that when targeting a framework that implicitly includes the references we need, no warning is generated.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_Net80()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80.WithNuGetConfigFilePath(MyReferenceAssemblies.NuGetConfigPath),
            NativeMethodsTxt = "CreateFile",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Gdi32()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80.WithNuGetConfigFilePath(MyReferenceAssemblies.NuGetConfigPath),
            LanguageVersion = LanguageVersion.CSharp12,
            NativeMethodsTxt = "gdi32.*",
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("BACKGROUND_MODE", "Windows.Win32.Graphics.Gdi.Apis.GetBkMode::return", "native storage is Int32, but the enum's underlying type is UInt32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("BACKGROUND_MODE", "Windows.Win32.Graphics.Gdi.Apis.SetBkMode::mode", "native storage is Int32, but the enum's underlying type is UInt32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("FONT_CHARSET", "Windows.Win32.Graphics.Gdi.Apis.CreateFontW::iCharSet", "native storage is UInt32, but the enum's underlying type is Byte"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("FONT_CLIP_PRECISION", "Windows.Win32.Graphics.Gdi.Apis.CreateFontW::iClipPrecision", "native storage is UInt32, but the enum's underlying type is Byte"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("FONT_OUTPUT_PRECISION", "Windows.Win32.Graphics.Gdi.Apis.CreateFontW::iOutPrecision", "native storage is UInt32, but the enum's underlying type is Byte"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("FONT_QUALITY", "Windows.Win32.Graphics.Gdi.Apis.CreateFontW::iQuality", "native storage is UInt32, but the enum's underlying type is Byte"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("GET_DEVICE_CAPS_INDEX", "Windows.Win32.Graphics.Gdi.Apis.GetDeviceCaps::index", "native storage is Int32, but the enum's underlying type is UInt32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("OBJ_TYPE", "Windows.Win32.Graphics.Gdi.Apis.GetCurrentObject::type", "native storage is UInt32, but the enum's underlying type is Int32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("PEN_STYLE", "Windows.Win32.Graphics.Gdi.Apis.ExtCreatePen::iPenStyle", "native storage is UInt32, but the enum's underlying type is Int32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("PEN_STYLE", "Windows.Win32.Graphics.Gdi.LOGPEN.lopnStyle", "native storage is UInt32, but the enum's underlying type is Int32"),
                new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                    .WithArguments("PFD_LAYER_TYPE", "Windows.Win32.Graphics.OpenGL.PIXELFORMATDESCRIPTOR.iLayerType", "native storage is Byte, but the enum's underlying type is SByte"),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NonUniqueWinmdProjectionNames()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "CreateFile",
            GeneratorConfiguration = GeneratorConfiguration.Default with
            {
                InputMetadataPaths = GeneratorConfiguration.Default.InputMetadataPaths.AddRange(GeneratorConfiguration.Default.InputMetadataPaths),
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.NonUniqueMetadataInputs.Id, DiagnosticSeverity.Error),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ArchSpecificApi_AnyCpu_ProducesError()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "MEMORY_BASIC_INFORMATION",
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.CpuArchitectureIncompatibility.Id, DiagnosticSeverity.Error).WithSpan("NativeMethods.txt", 1, 1, 1, 25),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("x64")]
    [InlineData("X86")]
    [InlineData("arm64")]
    public async Task ArchSpecificApi_AnyCpu_WithCsWin32PlatformTarget(string platformTarget)
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "MEMORY_BASIC_INFORMATION",
            GeneratorConfiguration = GeneratorConfiguration.Default with { PlatformTarget = platformTarget },
            TestState =
            {
                Sources = { "unsafe class Test { int Size = sizeof(Windows.Win32.System.Memory.MEMORY_BASIC_INFORMATION); }" },
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InvalidCsWin32PlatformTarget()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "CreateFile",
            GeneratorConfiguration = GeneratorConfiguration.Default with { PlatformTarget = "x65" },
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.InvalidPlatformTarget.Id, DiagnosticSeverity.Error),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
