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
        // Request DebugPropertyInfo and we should not see ITypeComp_unmanaged get generated.
        this.nativeMethods.Add("IAudioProcessingObjectConfiguration");
        await this.InvokeGeneratorAndCompile();

        var iface = this.FindGeneratedType("IAudioMediaType_unmanaged");
        Assert.Empty(iface);
    }

    [Fact]
    public async Task TestGenerateIUnknownAndID3D11DeviceContext()
    {
        // If IUnknown is requested first and then it's needed as an unmanaged type, we fail to generate it.
        this.nativeMethods.Add("IUnknown");
        this.nativeMethods.Add("ID3D11DeviceContext");
        await this.InvokeGeneratorAndCompile();
    }

    [Theory]
    [InlineData("CHAR", "Simple type")]
    [InlineData("RmRegisterResources", "Simple function")]
    [InlineData("IStream", "Interface with enum parameters that need to be marshaled as U4")]
    [InlineData("IServiceProvider", "exercises out parameter of type IUnknown marshaled to object")]
    [InlineData("CreateDispatcherQueueController", "Has WinRT object parameter which needs marshalling")]
    [InlineData("IEnumEventObject", "Derives from IDispatch")]
    [InlineData("DestroyIcon", "Exercise SetLastError on import")]
    [InlineData("IDebugProperty", "DebugPropertyInfo is weird")]
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
    [InlineData("ID3D11DeviceContext", "Problem with ppClassInstances parameter")]
    [InlineData("IBrowserService2", "Array return type needs marshaling attribute")]
    [InlineData("ID3D11VideoContext", "Parameter not properly annotated as marshaled")]
    [InlineData("ID2D1Factory4", "Function matches more than one interface member. Which interface member is actually chosen is implementation-dependent. Consider using a non-explicit implementation instead")]
    [InlineData("IDWriteFontFace5", "Pointers may only be used in an unsafe context")]
    [InlineData("ICorProfilerCallback11", "already defines a member called 'SurvivingReferences' with the same parameter types")]
    [InlineData("D3D11_VIDEO_DECODER_BUFFER_DESC1", "struct with pointer to struct")]
    [InlineData("D3D11_VIDEO_DECODER_EXTENSION", "struct with array of managed types")]
    public async Task TestGenerateApi(string api, string purpose)
    {
        this.Logger.WriteLine($"Testing {api} - {purpose}");
        this.nativeMethods.Add(api);
        await this.InvokeGeneratorAndCompile($"Test_{api}");
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
}
