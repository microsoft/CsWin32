// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402

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
}
