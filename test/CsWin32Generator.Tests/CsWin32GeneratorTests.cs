// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402,SA1201,SA1202

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        await this.InvokeGeneratorAndCompileFromFact();

        var idispatchType = this.FindGeneratedType("IDispatch");
        Assert.NotEmpty(idispatchType);
    }

    [Fact]
    public async Task TestGenerateIShellWindows()
    {
        this.nativeMethods.Add("IShellWindows");
        await this.InvokeGeneratorAndCompileFromFact();

        var ishellWindowsType = this.FindGeneratedType("IShellWindows");
        Assert.NotEmpty(ishellWindowsType);

        // Check that IShellWindows has IDispatch as a base
        Assert.Contains(ishellWindowsType, x => x.BaseList?.Types.Any(t => t.Type.ToString().Contains("IDispatch")) ?? false);
    }

    [Fact]
    public async Task TestNativeMethods()
    {
        this.nativeMethodsTxt = "NativeMethods.txt";
        await this.InvokeGeneratorAndCompileFromFact();
    }

    [Fact]
    public async Task CheckITypeCompIsUnmanaged()
    {
        // Request DebugPropertyInfo and we should see ITypeComp_unmanaged get generated because it has an embedded managed field
        this.nativeMethods.Add("DebugPropertyInfo");
        await this.InvokeGeneratorAndCompileFromFact();

        var iface = this.FindGeneratedType("ITypeComp_unmanaged");
        Assert.True(iface.Any());
    }

    [Fact]
    public async Task CheckIAudioProcessingObjectConfigurationDoesNotGenerateUnmanagedTypes()
    {
        // Request IAudioProcessingObjectConfiguration and it should request IAudioMediaType_unmanaged that's embedded in a struct
        this.nativeMethods.Add("IAudioProcessingObjectConfiguration");
        await this.InvokeGeneratorAndCompileFromFact();

        var iface = this.FindGeneratedType("IAudioMediaType_unmanaged");
        Assert.True(iface.Any());
    }

    [Fact]
    public async Task TestGenerateIUnknownAndID3D11DeviceContext()
    {
        // If IUnknown is requested first and then it's needed as an unmanaged type, we fail to generate it.
        this.nativeMethods.Add("IUnknown");
        this.nativeMethods.Add("ID3D11DeviceContext");
        await this.InvokeGeneratorAndCompileFromFact();
    }

    [Fact]
    public async Task TestGenerateSomethingInWin32System()
    {
        // If we need CharSet _and_ we generate something in Windows.Win32.System, the partially qualified reference breaks.
        this.nativeMethods.Add("GetDistanceOfClosestLanguageInList");
        this.nativeMethods.Add("ADVANCED_FEATURE_FLAGS");
        await this.InvokeGeneratorAndCompileFromFact();
    }

    [Theory]
    [InlineData("x64")]
    [InlineData("X64")]
    [InlineData("arm64")]
    [InlineData("ARM64")]
    public async Task TestPlatformCaseSensitivity(string platform)
    {
        this.platform = platform;
        this.nativeMethods.Add("SetWindowLongPtr");
        await this.InvokeGeneratorAndCompile($"{nameof(this.TestPlatformCaseSensitivity)}_{platform}");
    }

    [Fact]
    public async Task TestGenerateCoCreateableClass()
    {
        this.nativeMethods.Add("ShellLink");
        await this.InvokeGeneratorAndCompileFromFact();

        var shellLinkType = Assert.Single(this.FindGeneratedType("ShellLink"));

        // Check that it does not have the ComImport attribute.
        Assert.DoesNotContain(shellLinkType.AttributeLists, al => al.Attributes.Any(attr => attr.Name.ToString().Contains("ComImport")));

        // Check that it contains a CreateInstance method
        Assert.Contains(shellLinkType.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.Text == "CreateInstance");
    }

    [Theory]
    [InlineData("IMFMediaKeySession", "get_KeySystem", "winmdroot.Foundation.BSTR* keySystem")]
    [InlineData("AddPrinterW", "AddPrinter", "winmdroot.Foundation.PWSTR pName, uint Level, Span<byte> pPrinter")]
    public async Task VerifySignature(string api, string member, string signature)
    {
        // If we need CharSet _and_ we generate something in Windows.Win32.System, the partially qualified reference breaks.
        this.nativeMethods.Add(api);
        await this.InvokeGeneratorAndCompile($"{api}_{member}");

        var generatedMemberSignatures = this.FindGeneratedMethod(member).Select(x => x.ParameterList.ToString());
        Assert.Contains($"({signature})", generatedMemberSignatures);
    }

    public static IList<object[]> TestApiData => [
        ["CHAR", "Simple type"],
        ["RmRegisterResources", "Simple function"],
        ["IStream", "Interface with enum parameters that need to be marshaled as U4"],
        ["IServiceProvider", "exercises out parameter of type IUnknown marshaled to object"],
        ["CreateDispatcherQueueController", "Has WinRT object parameter which needs marshalling"],
        ["IEnumEventObject", "Derives from IDispatch"],
        ["DestroyIcon", "Exercise SetLastError on import"],
        ["IDebugProperty", "DebugPropertyInfo has a managed field"],
        ["GetThemeColor", "Tests enum marshaling to I4"],
        ["ChoosePixelFormat", "Tests marshaled field in struct"],
        ["IGraphicsEffectD2D1Interop", "Uses Uses IPropertyValue (not accessible in C#]"],
        ["Folder3", "Derives from multiple interfaces"],
        ["IAudioProcessingObjectConfiguration", "Test struct** parameter marshalling"],
        ["IBDA_EasMessage", "[In,[Out, should not be added"],
        ["ITypeComp", "ITypeComp should not be unmanaged"],
        ["AsyncIConnectedIdentityProvider", "Needs [In,[Out, on array parameter"],
        ["Column", "SYSLIB1092 that needs to be suppressed"],
        ["IBidiAsyncNotifyChannel", "Parameter that needs special marshaling help"],
        ["ID3D11Texture1D", "Unmanaged interface needs to not use `out` for any params"],
        ["ID3D11DeviceContext", "ppClassInstances parameter is annotated to have a size from uint* parameter"],
        ["IBrowserService2", "Array return type needs marshaling attribute"],
        ["ID3D11VideoContext", "Parameter not properly annotated as marshaled"],
        ["ID2D1Factory4", "Function matches more than one interface member. Which interface member is actually chosen is implementation-dependent. Consider using a non-explicit implementation instead"],
        ["IDWriteFontFace5", "Pointers may only be used in an unsafe context"],
        ["ICorProfilerCallback11", "already defines a member called 'SurvivingReferences' with the same parameter types"],
        ["D3D11_VIDEO_DECODER_BUFFER_DESC1", "struct with pointer to struct"],
        ["D3D11_VIDEO_DECODER_EXTENSION", "struct with array of managed types"],
        ["IWICBitmap", "Interface with multiple methods with same name"],
        ["ID3D12VideoDecodeCommandList1", "D3D12_VIDEO_DECODE_OUTPUT_STREAM_ARGUMENTS1 has an inline fixed length array of managed structs"],
        ["IDebugHostContext", "Marshaling bool without explicit marshaling information is not allowed"],
        ["HlinkCreateFromData", "IDataObject is not supported by source-generated COM"],
        ["IVPNotify", "IVPNotify derives from an interface that's missing a GUID"],
        ["PathGetCharType", "CharSet forwarded to another assembly"],
        ["ItsPubPlugin", "The platform 'windowsserver' is not a known platform name"],
        ["PFIND_MATCHING_THREAD", "Delegates can't have marshaling", TestOptions.GeneratesNothing],
        ["LPFNDFMCALLBACK", "Delegate return value can't be marshaled", TestOptions.GeneratesNothing],
        ["IDataObject", "Source generated COM can't rely on built-in IDataObject so make sure IDataObject generates correctly"],
        ["JsCreateRuntime", "Delegate has bool parameter which should not get MarshalAs(bool] added to it in blittable mode"],
        ["RoCreatePropertySetSerializer", "IPropertySetSerializer parameter doesn't get interface generated for it."],
        ["D3D9ON12_ARGS", "D3D9ON12_ARGS has an inline object[2, array in a struct"],
        ["Direct3DCreate9On12Ex", "D3D9ON12_ARGS argument is not supported for marshalling"],
        ["IDWriteGdiInterop1", "InterfaceImplementation already declares ABI_GetFontSignature"],
        ["IMFSinkWriterEx", "Introducing a 'Finalize' method can interfere with destructor invocation. Did you intend to declare a destructor?"],
        ["RoRegisterActivationFactories", "The type 'delegate* unmanaged[Stdcall,<HSTRING, IActivationFactory_unmanaged**, HRESULT>' may not be used as a type argument"],
        ["IMFMediaKeys", "cannot convert from 'Windows.Win32.Foundation.BSTR*' to 'object'"],
        ["ICompositorInterop2", "Needs type from UAP contract that isn't available"],
        ["SECURITY_NULL_SID_AUTHORITY", "static struct with embedded array incorrectly initialized"],
        ["CreateThreadpoolWork", "Friendly overload differs only on return type and 'in' modifiers on attributes"],
        ["GetModuleFileName", "Should have a friendly Span overload"],
    ];

    [Theory]
    [MemberData(nameof(TestApiData))]
    public async Task TestGenerateApi(string api, string purpose, TestOptions options = TestOptions.None)
    {
        await this.TestGenerateApiWorker(api, purpose, options, "net9.0");
    }

    [Theory]
    [MemberData(nameof(TestApiData))]
    public async Task TestGenerateApiNet8(string api, string purpose, TestOptions options = TestOptions.None)
    {
        await this.TestGenerateApiWorker(api, purpose, options, "net8.0");
    }

    private async Task TestGenerateApiWorker(string api, string purpose, TestOptions options, string tfm)
    {
        LanguageVersion langVersion = (tfm == "net8.0") ? LanguageVersion.CSharp12 : LanguageVersion.CSharp13;

        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(langVersion);
        this.Logger.WriteLine($"Testing {api} - {tfm} - {purpose}");
        this.nativeMethods.Add(api);
        await this.InvokeGeneratorAndCompile($"Test_{api}_{tfm}", options);
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
        await this.InvokeGeneratorAndCompileFromFact();
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
        await this.InvokeGeneratorAndCompile($"TestNativeMethodsExclusion_{scenario}", TestOptions.GeneratesNothing | TestOptions.DoNotFailOnDiagnostics);

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

    // https://github.com/microsoft/CsWin32/issues/1494
    [Theory]
    [InlineData(LanguageVersion.CSharp12)]
    [InlineData(LanguageVersion.CSharp13)]
    public async Task VerifyOverloadPriorityAttributeInNet8(LanguageVersion langVersion)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(langVersion);
        this.nativeMethods.Add("IShellLinkW");
        await this.InvokeGeneratorAndCompile($"{nameof(this.VerifyOverloadPriorityAttributeInNet8)}_{langVersion}");

        // See if we generated any methods with OverloadResolutionPriorityAttribute.
        var methods = this.compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>());
        var methodsWithAttribute = methods
            .Where(md => FindAttribute(md.AttributeLists, "OverloadResolutionPriority").Any());

        IEnumerable<AttributeSyntax> FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name) => attributeLists.SelectMany(al => al.Attributes).Where(a => a.Name.ToString() == name);

        if (langVersion >= LanguageVersion.CSharp13)
        {
            Assert.NotEmpty(methodsWithAttribute);
        }
        else
        {
            Assert.Empty(methodsWithAttribute);
        }
    }
}
