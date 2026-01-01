// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402,SA1201,SA1202,SA1515

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

        // And when generating IDispatch explicitly it should have "real" methods on it.
        var methods = idispatchType.SelectMany(t => t.DescendantNodes().OfType<MethodDeclarationSyntax>());
        var method = Assert.Single(methods, m => m.Identifier.Text == "GetTypeInfoCount");
        Assert.Contains("(out uint pctinfo)", method.ParameterList.ToString());

        var invokeMethods = methods.Where(m => m.Identifier.Text == "Invoke");
        Assert.All(invokeMethods, m => Assert.DoesNotContain("VARIANT_unmanaged", m.ParameterList.ToString()));
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

        var idispatchType = this.FindGeneratedType("IDispatch");
        Assert.NotEmpty(idispatchType);

        // And when generating IDispatch implicitly it should not have "real" methods on it.
        var methods = idispatchType.SelectMany(t => t.DescendantNodes().OfType<MethodDeclarationSyntax>());
        Assert.DoesNotContain(methods, m => m.Identifier.Text == "GetTypeInfoCount");
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
        // Request BINDPTR and we should see ITypeComp_unmanaged get generated because it has an embedded managed field
        this.nativeMethods.Add("BINDPTR");
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

    [Fact]
    public async Task InnerExceptionIsReportedOnParameterPlatformError()
    {
        this.nativeMethods.Add("SHGetFileInfo");
        this.platform = "AnyCPU";
        this.expectedExitCode = 1;
        await this.InvokeGeneratorAndCompile(nameof(this.InnerExceptionIsReportedOnParameterPlatformError), TestOptions.GeneratesNothing);
        Assert.Contains("Windows.Win32.UI.Shell.SHFILEINFOW is not declared for this platform.", this.Logger.Output);
    }

    [Fact]
    public async Task AllFriendlyOverloadsHaveTheSameAttributes()
    {
        this.nativeMethods.Add("SHGetFileInfo");
        this.platform = "arm64";
        await this.InvokeGeneratorAndCompileFromFact();
        var methods = this.FindGeneratedMethod("SHGetFileInfo");

        // Verify that all methods have the SupportedOSPlatform attribute
        Assert.All(methods, m => Assert.Contains(m.AttributeLists.First().Attributes, attr => attr.Name.ToString().Contains("SupportedOSPlatform") || attr.Name.ToString().Contains("DllImport") || attr.Name.ToString().Contains("LibraryImport")));
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
    public async Task CanGenerateWithNewerLanguageVersion()
    {
        this.languageVersionOverride = "20";
        this.nativeMethods.Add("SHGetFileInfo");
        await this.InvokeGeneratorAndCompileFromFact();
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

    [Fact]
    public async Task PointerReturnValueIsPreserved()
    {
        this.nativeMethods.Add("WTHelperProvDataFromStateData");
        await this.InvokeGeneratorAndCompileFromFact();
        var methodReturnTypes = this.FindGeneratedMethod("WTHelperProvDataFromStateData").Select(x => x.ReturnType.ToString());
        Assert.Contains("winmdroot.Security.WinTrust.CRYPT_PROVIDER_DATA*", methodReturnTypes);
    }

    [Fact]
    public async Task DelegatesGetStructsGenerated()
    {
        this.nativeMethods.Add("TIMERPROC");
        await this.InvokeGeneratorAndCompileFromFact();

        var timerProcType = Assert.Single(this.FindGeneratedType("TIMERPROC"));
    }

    public static IList<object[]> TestSignatureData => [
        ["IMFMediaKeySession", "get_KeySystem", "winmdroot.Foundation.BSTR* keySystem"],
        ["AddPrinterW", "AddPrinter", "[Optional] string pName, uint Level, Span<byte> pPrinter"],
        // MemorySized-struct param should have Span<byte> parameter.
        ["SHGetFileInfo", "SHGetFileInfo", "string pszPath, winmdroot.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES dwFileAttributes, [Optional] Span<byte> psfi, winmdroot.UI.Shell.SHGFI_FLAGS uFlags"],
        // MemorySized-struct param should also have a version with `ref struct` parameter.
        ["SHGetFileInfo", "SHGetFileInfo", "string pszPath, winmdroot.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES dwFileAttributes, ref winmdroot.UI.Shell.SHFILEINFOW psfi, winmdroot.UI.Shell.SHGFI_FLAGS uFlags"],
        ["InitializeAcl", "InitializeAcl", "Span<byte> pAcl, winmdroot.Security.ACE_REVISION dwAclRevision"],
        // MemorySized-struct param should also have a version with `out struct` parameter.
        ["InitializeAcl", "InitializeAcl", "out winmdroot.Security.ACL pAcl, winmdroot.Security.ACE_REVISION dwAclRevision"],
        ["SetDefaultCommConfig", "SetDefaultCommConfig", "string lpszName, in winmdroot.Devices.Communication.COMMCONFIG lpCC", false],
        ["ID3D11DeviceChild", "GetPrivateData", "this winmdroot.Graphics.Direct3D11.ID3D11DeviceChild @this, in global::System.Guid guid, ref uint pDataSize, [Optional] Span<byte> pData"],
        ["WriteFile", "WriteFile", "SafeHandle hFile, in byte lpBuffer, uint* lpNumberOfBytesWritten, global::System.Threading.NativeOverlapped* lpOverlapped", false],
        // All params included
        ["SetupDiGetDeviceInterfaceDetail", "SetupDiGetDeviceInterfaceDetail", "SafeHandle DeviceInfoSet, in winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, [Optional] Span<byte> DeviceInterfaceDetailData, out uint RequiredSize, ref winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVINFO_DATA DeviceInfoData"],
        // Optional params omitted
        ["SetupDiGetDeviceInterfaceDetail", "SetupDiGetDeviceInterfaceDetail", "SafeHandle DeviceInfoSet, in winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, [Optional] Span<byte> DeviceInterfaceDetailData"],
        // We should _not_ have a struct param overload for flexible arrays
        ["SetupDiGetDeviceInterfaceDetail", "SetupDiGetDeviceInterfaceDetail", "SafeHandle DeviceInfoSet, in winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, out winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVICE_INTERFACE_DETAIL_DATA_W DeviceInterfaceDetailData, out uint RequiredSize, ref winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVINFO_DATA DeviceInfoData", false],
        ["WinHttpReadData", "WinHttpReadData", "void* hRequest, Span<byte> lpBuffer, ref uint lpdwNumberOfBytesRead"],
        ["IsTextUnicode", "IsTextUnicode", "ReadOnlySpan<byte> lpv, ref winmdroot.Globalization.IS_TEXT_UNICODE_RESULT lpiResult"],
        // Omitted ref param
        ["IsTextUnicode", "IsTextUnicode", "ReadOnlySpan<byte> lpv"],
        ["GetAce", "GetAce", "in winmdroot.Security.ACL pAcl, uint dwAceIndex, out void* pAce"],
        // Optional and MemorySize-d struct params, optional params included
        ["SetupDiGetClassInstallParams", "SetupDiGetClassInstallParams", "SafeHandle DeviceInfoSet, [Optional] winmdroot.Devices.DeviceAndDriverInstallation.SP_DEVINFO_DATA? DeviceInfoData, [Optional] Span<byte> ClassInstallParams, out uint RequiredSize"],
        ["IEnumString", "Next", "this winmdroot.System.Com.IEnumString @this, Span<winmdroot.Foundation.PWSTR> rgelt, out uint pceltFetched"],
        ["PSCreateMemoryPropertyStore", "PSCreateMemoryPropertyStore", "in global::System.Guid riid, out void* ppv"],
        ["DeviceIoControl", "DeviceIoControl", "SafeHandle hDevice, uint dwIoControlCode, [Optional] ReadOnlySpan<byte> lpInBuffer, [Optional] Span<byte> lpOutBuffer, out uint lpBytesReturned, [Optional] global::System.Threading.NativeOverlapped* lpOverlapped"],
        ["DeviceIoControl", "DeviceIoControl", "SafeHandle hDevice, uint dwIoControlCode, [Optional] ReadOnlySpan<byte> lpInBuffer, [Optional] Span<byte> lpOutBuffer, out uint lpBytesReturned, [Optional] global::System.Threading.NativeOverlapped* lpOverlapped", true, "NativeMethods.IncludePointerOverloads.json"],
        ["NtQueryObject", "NtQueryObject", "[Optional] global::Windows.Win32.Foundation.HANDLE Handle, winmdroot.Foundation.OBJECT_INFORMATION_CLASS ObjectInformationClass, [Optional] Span<byte> ObjectInformation, out uint ReturnLength"],
        ["ITypeInfo", "GetFuncDesc", "uint index, out winmdroot.System.Com.FUNCDESC_unmanaged* ppFuncDesc"],
        ["ITsSbResourcePluginStore", "EnumerateTargets", "winmdroot.Foundation.BSTR FarmName, winmdroot.Foundation.BSTR EnvName, winmdroot.System.RemoteDesktop.TS_SB_SORT_BY sortByFieldId, winmdroot.Foundation.BSTR sortyByPropName, ref uint pdwCount, out winmdroot.System.RemoteDesktop.ITsSbTarget_unmanaged** pVal"],
        ["MFEnumDeviceSources", "MFEnumDeviceSources", "winmdroot.Media.MediaFoundation.IMFAttributes pAttributes, out winmdroot.Media.MediaFoundation.IMFActivate_unmanaged** pppSourceActivate, out uint pcSourceActivate"],
        // Check that GetObject optional parameters got an overload with marshalled interface types
        ["IWbemServices", "GetObject", "this winmdroot.System.Wmi.IWbemServices @this, SafeHandle strObjectPath, winmdroot.System.Wmi.WBEM_GENERIC_FLAG_TYPE lFlags, winmdroot.System.Wmi.IWbemContext pCtx, ref winmdroot.System.Wmi.IWbemClassObject ppObject, ref winmdroot.System.Wmi.IWbemCallResult ppCallResult"],
        // NativeOverlapped should be pointer even when not [Retained] as in CancelIoEx.
        ["CancelIoEx", "CancelIoEx", "SafeHandle hFile, [Optional] global::System.Threading.NativeOverlapped* lpOverlapped"],
        ["ITypeInfo", "GetNames", "this winmdroot.System.Com.ITypeInfo @this, int memid, Span<winmdroot.Foundation.BSTR> rgBstrNames, out uint pcNames"],
        ["EnumProcessModules", "EnumProcessModules", "SafeHandle hProcess, Span<byte> lphModule, out uint lpcbNeeded"],
        ["Windows.Win32.NetworkManagement.WindowsFilteringPlatform.FwpmProviderAdd0", "FwpmProviderAdd0", "SafeHandle engineHandle, in winmdroot.NetworkManagement.WindowsFilteringPlatform.FWPM_PROVIDER0 provider, [Optional] winmdroot.Security.PSECURITY_DESCRIPTOR sd"],
        // Verify the ABI signature has [Optional] on Optional and Reserved parameters.
        ["Windows.Win32.NetworkManagement.WindowsFilteringPlatform.FwpmEngineOpen0", "FwpmEngineOpen0", "[Optional] winmdroot.Foundation.PCWSTR serverName, uint authnService, [Optional] winmdroot.System.Rpc.SEC_WINNT_AUTH_IDENTITY_W* authIdentity, [Optional] winmdroot.NetworkManagement.WindowsFilteringPlatform.FWPM_SESSION0* session, winmdroot.Foundation.HANDLE* engineHandle"],
        // WlanCloseHandle accepts an additional reserved parameter. We can still generate safe hanlde for WlanOpenHandle then
        ["WlanOpenHandle", "WlanOpenHandle", "uint dwClientVersion, out uint pdwNegotiatedVersion, out global::Windows.Win32.WlanCloseHandleSafeHandle phClientHandle"],
    ];

    [Theory]
    [MemberData(nameof(TestSignatureData))]
    public async Task VerifySignature(string api, string member, string signature, bool assertPresent = true, string? nativeMethodsJson = null)
    {
        await this.VerifySignatureWorker(api, member, signature, assertPresent, "net9.0", nativeMethodsJson);
    }

    [Theory]
    [InlineData("InitializeAcl", "InitializeAcl", "out winmdroot.Security.ACL pAcl, winmdroot.Security.ACE_REVISION dwAclRevision", false)]
    public async Task VerifySignatureNet472(string api, string member, string signature, bool assertPresent = true, string? nativeMethodsJson = null)
    {
        await this.VerifySignatureWorker(api, member, signature, assertPresent, "net472", nativeMethodsJson);
    }

    private async Task VerifySignatureWorker(string api, string member, string signature, bool assertPresent, string tfm, string? nativeMethodsJson)
    {
        this.tfm = tfm;
        this.compilation = this.starterCompilations[tfm];
        this.nativeMethods.Add(api);
        this.nativeMethodsJson = nativeMethodsJson;

        // Make a unique name based on the signature
        await this.InvokeGeneratorAndCompile($"{api}_{member}_{tfm}_{signature.Select(x => (int)x).Aggregate((x, y) => x + y):X}");

        var generatedMemberSignatures = this.FindGeneratedMethod(member).Select(x => x.ParameterList.ToString()).ToArray();

        foreach (var generatedSignature in generatedMemberSignatures)
        {
            this.Logger.WriteLine(generatedSignature.ToString());
        }

        this.Logger.WriteLine("------ searching for -------");
        this.Logger.WriteLine($"({signature})");

        if (assertPresent)
        {
            Assert.Contains($"({signature})", generatedMemberSignatures);
        }
        else
        {
            Assert.DoesNotContain($"({signature})", generatedMemberSignatures);
        }
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
        ["PdhGetCounterInfo", "Optional out parameter omission conflicts with other overload"],
        ["RtlUpcaseUnicodeChar", "char parameter should not get CharSet marshalling in AOT"],
        ["CryptGetAsyncParam", "Has optional unmanaged delegate out param"],
        ["NtQueryObject", "Verify pointer overloads and optional parameters", TestOptions.None, "NativeMethods.IncludePointerOverloads.json"],
    ];

    [Theory]
    [MemberData(nameof(TestApiData))]
    public async Task TestGenerateApiNet10(string api, string purpose, TestOptions options = TestOptions.None, string? nativeMethodsJson = null)
    {
        await this.TestGenerateApiWorker(api, purpose, options, "net10.0", nativeMethodsJson);
    }

    [Theory]
    [MemberData(nameof(TestApiData))]
    public async Task TestGenerateApiNet9(string api, string purpose, TestOptions options = TestOptions.None, string? nativeMethodsJson = null)
    {
        await this.TestGenerateApiWorker(api, purpose, options, "net9.0", nativeMethodsJson);
    }

    [Theory]
    [MemberData(nameof(TestApiData))]
    public async Task TestGenerateApiNet8(string api, string purpose, TestOptions options = TestOptions.None, string? nativeMethodsJson = null)
    {
        await this.TestGenerateApiWorker(api, purpose, options, "net8.0", nativeMethodsJson);
    }

    private async Task TestGenerateApiWorker(string api, string purpose, TestOptions options, string tfm, string? nativeMethodsJson)
    {
        LanguageVersion langVersion = tfm switch
        {
            "net8.0" => LanguageVersion.CSharp12,
            "net9.0" => LanguageVersion.CSharp13,
            "net10.0" => LanguageVersion.CSharp14,
            _ => throw new InvalidOperationException(),
        };

        this.tfm = tfm;
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(langVersion);
        this.Logger.WriteLine($"Testing {api} - {tfm} - {purpose}");
        this.nativeMethods.Add(api);
        this.nativeMethodsJson = nativeMethodsJson;
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
#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436,CS8981,SYSLIB1092
using global::System;
using global::System.Diagnostics;
using global::System.Diagnostics.CodeAnalysis;
using global::System.Runtime.CompilerServices;
using global::System.Runtime.InteropServices;
using global::System.Runtime.Versioning;

                    [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(""{friendName}"")]

                    namespace Windows.Win32.Foundation
                    {{
		                internal unsafe readonly partial struct PCWSTR
			                : IEquatable<PCWSTR>
		                {{
			                internal readonly char* Value;

			                internal PCWSTR(char* value) => this.Value = value;

			                public static explicit operator char*(PCWSTR value) => value.Value;

			                public static implicit operator PCWSTR(char* value) => new PCWSTR(value);

			                public bool Equals(PCWSTR other) => this.Value == other.Value;

			                public override bool Equals(object obj) => obj is PCWSTR other && this.Equals(other);

			                public override int GetHashCode() => unchecked((int)this.Value);

			                internal int Length
			                {{
				                get
				                {{
					                char* p = this.Value;
					                if (p is null)
						                return 0;
					                while (*p != '\0')
						                p++;
					                return checked((int)(p - this.Value));
				                }}
			                }}


			                public override string ToString() => this.Value is null ? null : new string(this.Value);

			                internal ReadOnlySpan<char> AsSpan() => this.Value is null ? default(ReadOnlySpan<char>) : new ReadOnlySpan<char>(this.Value, this.Length);

			                private string DebuggerDisplay => this.ToString();
		                }}
                    }}

                    namespace Windows.Win32
                    {{
	                    internal partial class SysFreeStringSafeHandle :SafeHandle	{{
		                    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(0L);

		                    internal SysFreeStringSafeHandle():base(INVALID_HANDLE_VALUE, true)
		                    {{
		                    }}

		                    internal SysFreeStringSafeHandle(IntPtr preexistingHandle, bool ownsHandle = true):base(INVALID_HANDLE_VALUE, ownsHandle)
		                    {{
			                    this.SetHandle(preexistingHandle);
		                    }}

		                    public override bool IsInvalid => false;

		                    protected override bool ReleaseHandle()
		                    {{
			                    return true;
		                    }}
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
        this.nativeMethods.Add("StrToIntW"); // Method that uses PCWSTR
        this.nativeMethods.Add("IRestrictedErrorInfo"); // Generates BSTR out params and makes SysFreeStringSafeHandle
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
        Assert.Empty(this.FindGeneratedType("SysFreeStringSafeHandle"));

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

    [Theory, CombinatorialData]
    public async Task CrossWinMD_IInspectable(
        [CombinatorialValues([false, true])] bool allowMarshaling,
        [CombinatorialValues([null, "TestPInvoke"])] string pinvokeClassName,
        [CombinatorialValues(["net8.0", "net9.0", "net10.0"])] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.win32winmdPaths = [.. this.win32winmdPaths!, CustomIInspectableMetadataPath];
        this.nativeMethodsJsonOptions = new NativeMethodsJsonOptions
        {
            AllowMarshaling = allowMarshaling,
            ClassName = pinvokeClassName,
        };
        this.nativeMethods.Add("ITestDerivedFromInspectable");
        await this.InvokeGeneratorAndCompile($"{nameof(this.CrossWinMD_IInspectable)}_{tfm}_{allowMarshaling}_{pinvokeClassName ?? "null"}");
    }

    [Fact]
    public async Task TestComVariantReturnValue()
    {
        // IUIAutomationElement has methods that return VARIANT, they should be translated to ComVariant
        this.nativeMethods.Add("IUIAutomationElement");
        await this.InvokeGeneratorAndCompileFromFact();

        var iface = this.FindGeneratedType("IUIAutomationElement");
        Assert.NotEmpty(iface);

        // And when generating IDispatch explicitly it should have "real" methods on it.
        var methods = iface.SelectMany(t => t.DescendantNodes().OfType<MethodDeclarationSyntax>());
        var method = Assert.Single(methods, m => m.Identifier.Text == "GetCachedPropertyValue");
        Assert.Contains("ComVariant", method.ReturnType.ToString());
    }
}
