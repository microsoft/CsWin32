// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsWin32Generator.Tests;

public partial class CsWin32GeneratorTests : CsWin32GeneratorTestsBase
{
    public CsWin32GeneratorTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task TestGenerateIDispatch()
    {
        // IDispatch is not normally emitted, but we need it for source generated com so check that it got generated.
        this.nativeMethods.Add("IDispatch");
        await this.InvokeGeneratorAndCompile();

        var idispatchType = this.FindGeneratedType("IDispatch");
        Assert.NotEmpty(idispatchType);
    }

    [Fact]
    public async Task TestGenerateIShellWindows()
    {
        this.nativeMethods.Add("IShellWindows");
        await this.InvokeGeneratorAndCompile();

        var ishellWindowsType = this.FindGeneratedType("IShellWindows");
        Assert.NotEmpty(ishellWindowsType);

        // Check that IShellWindows has IDispatch as a base
        Assert.Contains(ishellWindowsType, x => x.BaseList?.Types.Any(t => t.Type.ToString().Contains("IDispatch")) ?? false);
    }

    [Fact]
    public async Task TestNativeMethods()
    {
        this.nativeMethodsTxt = "NativeMethods.txt";
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task CheckITypeCompIsUnmanaged()
    {
        // Request DebugPropertyInfo and we should see ITypeComp_unmanaged get generated because it has an embedded managed field
        this.nativeMethods.Add("DebugPropertyInfo");
        await this.InvokeGeneratorAndCompile();

        var iface = this.FindGeneratedType("ITypeComp_unmanaged");
        Assert.True(iface.Any());
    }

    [Fact]
    public async Task CheckIAudioProcessingObjectConfigurationDoesNotGenerateUnmanagedTypes()
    {
        // Request IAudioProcessingObjectConfiguration and it should request IAudioMediaType_unmanaged that's embedded in a struct
        this.nativeMethods.Add("IAudioProcessingObjectConfiguration");
        await this.InvokeGeneratorAndCompile();

        var iface = this.FindGeneratedType("IAudioMediaType_unmanaged");
        Assert.True(iface.Any());
    }

    [Fact]
    public async Task TestGenerateIUnknownAndID3D11DeviceContext()
    {
        // If IUnknown is requested first and then it's needed as an unmanaged type, we fail to generate it.
        this.nativeMethods.Add("IUnknown");
        this.nativeMethods.Add("ID3D11DeviceContext");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task TestGenerateSomethingInWin32System()
    {
        // If we need CharSet _and_ we generate something in Windows.Win32.System, the partially qualified reference breaks.
        this.nativeMethods.Add("GetDistanceOfClosestLanguageInList");
        this.nativeMethods.Add("ADVANCED_FEATURE_FLAGS");
        await this.InvokeGeneratorAndCompile();
    }

    [Theory]
    [InlineData("IMFMediaKeySession", "get_KeySystem", "winmdroot.Foundation.BSTR* keySystem")]
    [InlineData("AddPrinterW", "AddPrinter", "winmdroot.Foundation.PWSTR pName, uint Level, Span<byte> pPrinter")]
    public async Task VerifySignature(string api, string member, string signature)
    {
        // If we need CharSet _and_ we generate something in Windows.Win32.System, the partially qualified reference breaks.
        this.nativeMethods.Add(api);
        await this.InvokeGeneratorAndCompile(TestOptions.None, $"{api}_{member}");

        var generatedMemberSignatures = this.FindGeneratedMethod(member).Select(x => x.ParameterList.ToString());
        Assert.Contains($"({signature})", generatedMemberSignatures);
    }

    [Theory]
    [InlineData("CHAR", "Simple type")]
    [InlineData("RmRegisterResources", "Simple function")]
    [InlineData("IStream", "Interface with enum parameters that need to be marshaled as U4")]
    [InlineData("IServiceProvider", "exercises out parameter of type IUnknown marshaled to object")]
    [InlineData("CreateDispatcherQueueController", "Has WinRT object parameter which needs marshalling")]
    [InlineData("IEnumEventObject", "Derives from IDispatch")]
    [InlineData("DestroyIcon", "Exercise SetLastError on import")]
    [InlineData("IDebugProperty", "DebugPropertyInfo has a managed field")]
    [InlineData("GetThemeColor", "Tests enum marshaling to I4")]
    [InlineData("ChoosePixelFormat", "Tests marshaled field in struct")]
    [InlineData("IGraphicsEffectD2D1Interop", "Uses Uses IPropertyValue (not accessible in C#)")]
    [InlineData("Folder3", "Derives from multiple interfaces")]
    [InlineData("IAudioProcessingObjectConfiguration", "Test struct** parameter marshalling")]
    [InlineData("IBDA_EasMessage", "[In][Out] should not be added")]
    [InlineData("ITypeComp", "ITypeComp should not be unmanaged")]
    [InlineData("AsyncIConnectedIdentityProvider", "Needs [In][Out] on array parameter")]
    [InlineData("Column", "SYSLIB1092 that needs to be suppressed")]
    [InlineData("IBidiAsyncNotifyChannel", "Parameter that needs special marshaling help")]
    [InlineData("ID3D11Texture1D", "Unmanaged interface needs to not use `out` for any params")]
    [InlineData("ID3D11DeviceContext", "ppClassInstances parameter is annotated to have a size from uint* parameter")]
    [InlineData("IBrowserService2", "Array return type needs marshaling attribute")]
    [InlineData("ID3D11VideoContext", "Parameter not properly annotated as marshaled")]
    [InlineData("ID2D1Factory4", "Function matches more than one interface member. Which interface member is actually chosen is implementation-dependent. Consider using a non-explicit implementation instead")]
    [InlineData("IDWriteFontFace5", "Pointers may only be used in an unsafe context")]
    [InlineData("ICorProfilerCallback11", "already defines a member called 'SurvivingReferences' with the same parameter types")]
    [InlineData("D3D11_VIDEO_DECODER_BUFFER_DESC1", "struct with pointer to struct")]
    [InlineData("D3D11_VIDEO_DECODER_EXTENSION", "struct with array of managed types")]
    [InlineData("IWICBitmap", "Interface with multiple methods with same name")]
    [InlineData("ID3D12VideoDecodeCommandList1", "D3D12_VIDEO_DECODE_OUTPUT_STREAM_ARGUMENTS1 has an inline fixed length array of managed structs")]
    [InlineData("IDebugHostContext", "Marshaling bool without explicit marshaling information is not allowed")]
    [InlineData("HlinkCreateFromData", "IDataObject is not supported by source-generated COM")]
    [InlineData("IVPNotify", "IVPNotify derives from an interface that's missing a GUID")]
    [InlineData("PathGetCharType", "CharSet forwarded to another assembly")]
    [InlineData("ItsPubPlugin", "The platform 'windowsserver' is not a known platform name")]
    [InlineData("PFIND_MATCHING_THREAD", "Delegates can't have marshaling", TestOptions.GeneratesNothing)]
    [InlineData("LPFNDFMCALLBACK", "Delegate return value can't be marshaled", TestOptions.GeneratesNothing)]
    [InlineData("IDataObject", "Source generated COM can't rely on built-in IDataObject so make sure IDataObject generates correctly")]
    [InlineData("JsCreateRuntime", "Delegate has bool parameter which should not get MarshalAs(bool) added to it in blittable mode")]
    [InlineData("RoCreatePropertySetSerializer", "IPropertySetSerializer parameter doesn't get interface generated for it.")]
    [InlineData("D3D9ON12_ARGS", "D3D9ON12_ARGS has an inline object[2] array in a struct")]
    [InlineData("Direct3DCreate9On12Ex", "D3D9ON12_ARGS argument is not supported for marshalling")]
    [InlineData("IDWriteGdiInterop1", "InterfaceImplementation already declares ABI_GetFontSignature")]
    [InlineData("IMFSinkWriterEx", "Introducing a 'Finalize' method can interfere with destructor invocation. Did you intend to declare a destructor?")]
    [InlineData("RoRegisterActivationFactories", "The type 'delegate* unmanaged[Stdcall]<HSTRING, IActivationFactory_unmanaged**, HRESULT>' may not be used as a type argument")]
    [InlineData("IMFMediaKeys", "cannot convert from 'Windows.Win32.Foundation.BSTR*' to 'object'")]
    [InlineData("ICompositorInterop2", "Needs type from UAP contract that isn't available")]
    [InlineData("SECURITY_NULL_SID_AUTHORITY", "static struct with embedded array incorrectly initialized")]
    [InlineData("CreateThreadpoolWork", "Friendly overload differs only on return type and 'in' modifiers on attributes")]
    [InlineData("GetModuleFileName", "Should have a friendly Span overload")]
    public async Task TestGenerateApi(string api, string purpose, TestOptions options = TestOptions.None)
    {
        this.Logger.WriteLine($"Testing {api} - {purpose}");
        this.nativeMethods.Add(api);
        await this.InvokeGeneratorAndCompile(options, $"Test_{api}");
    }

    [Fact]
    public async Task CommandLineTool_ShowsError_WhenNativeMethodsTxtMissing()
    {
        // Arrange
        string missingNativeMethodsTxtPath = Path.Combine("test", "NonExistent", "NativeMethods.txt");
        string outputPath = Path.Combine(Path.GetTempPath(), "CsWin32GeneratorTests_Output3");
        Directory.CreateDirectory(outputPath);

        // Act
        int exitCode = await CsWin32Generator.Program.Main(new[]
        {
            "--native-methods-txt", missingNativeMethodsTxtPath,
            "--output-path", outputPath,
        });

        // Assert
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task GenerateCommandLineCommands_WithNativeMethodsJsonContainingCommentAndTrailingComma_HandlesCorrectly()
    {
        // Arrange
        this.nativeMethodsJson = "NativeMethodsWithCommentAndComma.json";
        this.nativeMethods.Add("IUnknown"); // Add a method to ensure generation occurs

        // Act
        await this.InvokeGeneratorAndCompile();
    }

    [Theory]
    [InlineData("BSTR", new[] { "BSTR" }, new[] { "BSTR" }, "BSTR")]
    [InlineData("BSTR_full", new[] { "BSTR" }, new[] { "Windows.Win32.Foundation.BSTR" }, "BSTR")]
    [InlineData("Column_without_BSTR", new[] { "Column" }, new[] { "BSTR" }, "BSTR")]
    [InlineData("ChoosePixelFormat_without_PFD", new[] { "ChoosePixelFormat" }, new[] { "Windows.Win32.Graphics.OpenGL.PFD.*" }, "PFD_FLAGS")]
    [InlineData("GetModuleHandle_without_SafeHandle", new[] { "GetModuleHandle" }, new[] { "FreeLibrarySafeHandle" }, "FreeLibrarySafeHandle")]
    public async Task TestNativeMethodsExclusion(string scenario, string[] includes, string[] excludes, string checkNotPresent)
    {
        this.nativeMethodsJson = "NativeMethods.json";

        // Add includes to nativeMethods
        foreach (var include in includes)
        {
            this.nativeMethods.Add(include);
        }

        // Add excludes to nativeMethods with "-" prefix
        foreach (var exclude in excludes)
        {
            this.nativeMethods.Add($"-{exclude}");
        }

        // Invoke the generator and compile
        await this.InvokeGeneratorAndCompile(TestOptions.GeneratesNothing | TestOptions.DoNotFailOnDiagnostics, $"TestNativeMethodsExclusion_{scenario}");

        // Verify the results based on includes and excludes
        Assert.Empty(this.FindGeneratedType(checkNotPresent));
    }

    [Theory, PairwiseData]
    public async Task DoNotEmitTypesFromInternalsVisibleToReferences(bool strongNameSign)
    {
        // Create a reference assembly with an internal type that has InternalsVisibleTo
        string referencedAssemblyName = "ReferencedAssembly";
        string referencingAssemblyName = "TestAssembly";

        string strongNameKeyFilePath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", "TestKey.snk");

        string friendName = strongNameSign ?
            $"{referencingAssemblyName}, PublicKey=0024000004800000940000000602000000240000525341310004000001000100e5999fa77c961399a0969d58b6889d88b0abb6bc639762ae519e94deb639d9169db63d972d351368a893f82adc11ac9c520a945e8806aed1f06f5db55a458ec81365b40d7b940c35e8c285683646e4a632e436089f19c378bf9a27f201b32614be5bb3064f01c3b798856b39ecfb8229b497584254a1cd42fa9cd543fd6bc0c8" :
            referencingAssemblyName;

        // Create a compilation for the referenced assembly with internal PCWSTR type and InternalsVisibleTo
        CSharpCompilation referencedCompilation = this.compilation
            .WithAssemblyName(referencedAssemblyName)
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    $@"
                    [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(""{friendName}"")]

                    namespace Windows.Win32.Foundation
                    {{
                        internal struct PCWSTR
                        {{        
                            // Field exists solely to validate that the containing type is considered non-struct-like.
                            internal unsafe byte* Value;
                        }}
                    }}
                    ",
                    this.parseOptions,
                    cancellationToken: TestContext.Current.CancellationToken));
        if (strongNameSign)
        {
            referencedCompilation = referencedCompilation.WithOptions(
                this.compilation.Options
                    .WithStrongNameProvider(new DesktopStrongNameProvider())
                    .WithCryptoKeyFile(strongNameKeyFilePath));
        }

        var diagnostics = referencedCompilation.GetDiagnostics(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(diagnostics);

        // Verify the referenced assembly compiles
        string referencedAssemblyPath = $"{Path.GetTempFileName()}-{referencedAssemblyName}.dll";
        var referencedEmitResult = referencedCompilation.Emit(referencedAssemblyPath, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(referencedEmitResult.Success, "Referenced assembly should compile successfully");

        this.nativeMethods.Add("PCWSTR");
        this.nativeMethods.Add("GetTickCount");
        this.additionalReferences.Add(referencedAssemblyPath);
        this.assemblyName = referencingAssemblyName;
        this.keyFile = strongNameSign ? strongNameKeyFilePath : null;

        if (strongNameSign)
        {
            this.compilation = this.compilation.WithOptions(
                this.compilation.Options
                    .WithStrongNameProvider(new DesktopStrongNameProvider())
                    .WithCryptoKeyFile(strongNameKeyFilePath));
        }

        await this.InvokeGeneratorAndCompile(testCase: $"{nameof(this.DoNotEmitTypesFromInternalsVisibleToReferences)}_{strongNameSign}");

        Assert.Empty(this.FindGeneratedType("PCWSTR"));

        File.Delete(referencedAssemblyPath);
    }
}
