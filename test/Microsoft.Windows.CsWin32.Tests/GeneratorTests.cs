// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Windows.CsWin32;
using Microsoft.Windows.CsWin32.Tests;
using Xunit;
using Xunit.Abstractions;
using VerifyTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpSourceGeneratorTest<
    Microsoft.Windows.CsWin32.SourceGenerator,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

public class GeneratorTests : IDisposable, IAsyncLifetime
{
    private const string WinRTCustomMarshalerClass = "WinRTCustomMarshaler";
    private const string WinRTCustomMarshalerNamespace = "Windows.Win32.CsWin32.InteropServices";
    private const string WinRTCustomMarshalerFullName = WinRTCustomMarshalerNamespace + "." + WinRTCustomMarshalerClass;

    private static readonly GeneratorOptions DefaultTestGeneratorOptions = new GeneratorOptions { EmitSingleFile = true };
    private static readonly string FileSeparator = new string('=', 140);
    private static readonly string MetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Win32.winmd");
    ////private static readonly string DiaMetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Microsoft.Dia.winmd");
    private static readonly string ApiDocsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "apidocs.msgpack");
    private readonly ITestOutputHelper logger;
    private readonly Dictionary<string, CSharpCompilation> starterCompilations = new();
    private CSharpCompilation compilation;
    private CSharpParseOptions parseOptions;
    private Generator? generator;

    public GeneratorTests(ITestOutputHelper logger)
    {
        this.logger = logger;

        this.parseOptions = CSharpParseOptions.Default
            .WithDocumentationMode(DocumentationMode.Diagnose)
            .WithLanguageVersion(LanguageVersion.CSharp9);

        // set in InitializeAsync
        this.compilation = null!;
    }

    public static IEnumerable<object[]> TFMData =>
        new object[][]
        {
            new object[] { "net40" },
            new object[] { "netstandard2.0" },
            new object[] { "net5.0" },
        };

    public async Task InitializeAsync()
    {
        this.starterCompilations.Add("net40", await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net40));
        this.starterCompilations.Add("netstandard2.0", await this.CreateCompilationAsync(MyReferenceAssemblies.NetStandard20));
        this.starterCompilations.Add("net5.0", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net50));
        this.starterCompilations.Add("net5.0-x86", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net50, Platform.X86));
        this.starterCompilations.Add("net5.0-x64", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net50, Platform.X64));

        this.compilation = this.starterCompilations["netstandard2.0"];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        this.generator?.Dispose();
    }

    [Theory]
    [InlineData("COPYFILE2_CALLBACK_NONE", "COPYFILE2_MESSAGE_TYPE")]
    [InlineData("RTL_RUN_ONCE_ASYNC", null)]
    [InlineData("__zz__not_defined", null)]
    public void TryGetEnumName(string candidate, string? declaringEnum)
    {
        this.generator = this.CreateGenerator();

        Assert.Equal(declaringEnum is object, this.generator.TryGetEnumName(candidate, out string? actualDeclaringEnum));
        Assert.Equal(declaringEnum, actualDeclaringEnum);
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void SimplestMethod(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator();
        const string methodName = "GetTickCount";
        Assert.True(this.generator.TryGenerateExternMethod(methodName, out _));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var generatedMethod = this.FindGeneratedMethod(methodName).Single();
        if (tfm == "net5.0")
        {
            Assert.Contains(generatedMethod.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
        else
        {
            Assert.DoesNotContain(generatedMethod.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }

        if (tfm != "net40")
        {
            Assert.Contains(generatedMethod.AttributeLists, al => IsAttributePresent(al, "DefaultDllImportSearchPaths"));
        }
    }

    [Fact]
    public void SupportedOSPlatform_AppearsOnFriendlyOverloads()
    {
        const string methodName = "GetStagedPackagePathByFullName2";
        this.compilation = this.starterCompilations["net5.0"];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.All(this.FindGeneratedMethod(methodName), method => Assert.Contains(method.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform")));
    }

    [Theory, PairwiseData]
    public void COMInterfaceWithSupportedOSPlatform(bool net50, bool allowMarshaling)
    {
        this.compilation = this.starterCompilations[net50 ? "net5.0" : "netstandard2.0"];
        const string typeName = "IInkCursors";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerateType(typeName));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var iface = this.FindGeneratedType(typeName).Single();

        if (net50 && !allowMarshaling)
        {
            Assert.Contains(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
        else
        {
            Assert.DoesNotContain(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
    }

    [Theory]
    [CombinatorialData]
    public void InterestingAPIs(
        [CombinatorialValues(
            "CreateFile", // SafeHandle-derived type
            "D3DGetTraceInstructionOffsets", // SizeParamIndex
            "PlgBlt", // SizeConst
            "ENABLE_TRACE_PARAMETERS_V1", // bad xml created at some point.
            "JsRuntimeVersion", // An enum that has an extra member in a separate header file.
            "ReportEvent", // Failed at one point
            "DISPLAYCONFIG_VIDEO_SIGNAL_INFO", // Union, explicit layout, bitmask, nested structs
            "g_wszStreamBufferRecordingDuration", // Constant string field
            "MFVideoAlphaBitmap", // field named params
            "DDRAWI_DDVIDEOPORT_INT", // field that is never used
            "MainAVIHeader", // dwReserved field is a fixed length array
            "HBMMENU_POPUP_RESTORE", // A HBITMAP handle as a constant
            "RpcServerRegisterIfEx", // Optional attribute on delegate type.
            "RpcSsSwapClientAllocFree", // Parameters typed as pointers to in delegates and out delegates
            "RPC_DISPATCH_TABLE", // Struct with a field typed as a delegate
            "RPC_SERVER_INTERFACE", // Struct with a field typed as struct with a field typed as a delegate
            "DDHAL_DESTROYDRIVERDATA", // Struct with a field typed as a delegate
            "I_RpcServerInqAddressChangeFn", // p/invoke that returns a function pointer
            "WSPUPCALLTABLE", // a delegate with a delegate in its signature
            "HWND_BOTTOM", // A constant typed as a typedef'd struct
            "BOOL", // a special cased typedef struct
            "uregex_getMatchCallback", // friendly overload with delegate parameter, and out parameters
            "CreateDispatcherQueueController", // References a WinRT type
            "RegOpenKey", // allocates a handle with a release function that returns LSTATUS
            "LsaRegisterLogonProcess", // allocates a handle with a release function that returns NTSTATUS
            "FilterCreate", // allocates a handle with a release function that returns HRESULT
            "DsGetDcOpen", // allocates a handle with a release function that returns HRESULT
            "DXVAHDSW_CALLBACKS", // pointers to handles
            "HBITMAP_UserMarshal", // in+out handle pointer
            "GetDiskFreeSpaceExW", // ULARGE_INTEGER replaced with keyword: ulong.
            "MsiGetProductPropertyW", // MSIHANDLE (a 32-bit handle)
            "tcp_opt_sack", // nested structs with inline arrays with nested struct elements
            "HANDLETABLE", // nested structs with inline arrays with nint element
            "SYSTEM_POLICY_INFORMATION", // nested structs with inline arrays with IntPtr element
            "D3D11_BLEND_DESC1", // nested structs with inline arrays with element that is NOT nested
            "RTM_DEST_INFO", // nested structs with inline arrays with element whose name collides with another
            "DISPPARAMS",
            "CoCreateInstance", // a hand-written friendly overload
            "JsVariantToValue",
            "D2D1_DEFAULT_FLATTENING_TOLERANCE", // a float constant
            "WIA_CATEGORY_FINISHED_FILE", // GUID constant
            "DEVPKEY_MTPBTH_IsConnected", // PROPERTYKEY constant
            "RT_CURSOR", // PCWSTR constant
            "IOleUILinkContainerW", // An IUnknown-derived interface with no GUID
            "RTM_ENTITY_EXPORT_METHODS",
            "FILE_TYPE_NOTIFICATION_INPUT",
            "DS_SELECTION_LIST", // A struct with a fixed-length inline array of potentially managed structs
            "ISpellCheckerFactory", // COM interface that includes `ref` parameters
            "LocalSystemTimeToLocalFileTime", // small step
            "WSAHtons", // A method that references SOCKET (which is typed as UIntPtr) so that a SafeHandle will be generated.
            "IDelayedPropertyStoreFactory", // interface inheritance across namespaces
            "ID3D12Resource", // COM interface with base types
            "ID2D1RectangleGeometry")] // COM interface with base types
        string api,
        bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with
        {
            WideCharOnly = false,
            AllowMarshaling = allowMarshaling,
        };
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Verifies that GetLastError is never generated.
    /// Users should call <see cref="Marshal.GetLastWin32Error"/> instead.
    /// </summary>
    [Fact]
    public void GetLastErrorNotIncludedInBulkGeneration()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("kernel32.*", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        Assert.True(this.IsMethodGenerated("CreateFile"));
        Assert.False(this.IsMethodGenerated("GetLastError"));
    }

    [Fact]
    public void FriendlyOverloadOfCOMInterfaceRemovesParameter()
    {
        const string ifaceName = "IEnumDebugPropertyInfo";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedMethod("Next"), m => m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[0].Modifiers.Any(SyntaxKind.ThisKeyword));
    }

    [Fact]
    public void IDispatchDerivedInterface()
    {
        const string ifaceName = "IInkRectangle";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Contains(this.FindGeneratedType(ifaceName), t => t.BaseList is null && t.AttributeLists.Any(al => al.Attributes.Any(a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "InterfaceType" } } && a.ArgumentList?.Arguments[0].Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: nameof(ComInterfaceType.InterfaceIsIDispatch) } } })));
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Fact]
    public void IInpectableDerivedInterface()
    {
        const string ifaceName = "IUserConsentVerifierInterop";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedType(ifaceName), t => t.BaseList is null && ((InterfaceDeclarationSyntax)t).Members.Count == 1 && t.AttributeLists.Any(al => al.Attributes.Any(a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "InterfaceType" } } && a.ArgumentList?.Arguments[0].Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: nameof(ComInterfaceType.InterfaceIsIInspectable) } } })));

        // Make sure the WinRT marshaler was not brought in
        Assert.Empty(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void WinRTInterfaceDoesntBringInMarshalerIfParamNotObject()
    {
        const string WinRTInteropInterfaceName = "IGraphicsEffectD2D1Interop";

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(WinRTInteropInterfaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Make sure the WinRT marshaler was not brought in
        Assert.Empty(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void WinRTInterfaceWithWinRTOutObjectUsesMarshaler()
    {
        const string WinRTInteropInterfaceName = "ICompositorDesktopInterop";
        const string WinRTClassName = "Windows.UI.Composition.Desktop.DesktopWindowTarget";

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(WinRTInteropInterfaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        InterfaceDeclarationSyntax interfaceDeclaration = (InterfaceDeclarationSyntax)Assert.Single(this.FindGeneratedType(WinRTInteropInterfaceName));
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)interfaceDeclaration.Members.First();
        ParameterSyntax lastParam = method.ParameterList.Parameters.Last();

        Assert.Equal(WinRTClassName, lastParam.Type?.ToString());
        Assert.True(lastParam.Modifiers.Any(SyntaxKind.OutKeyword));

        AttributeSyntax marshalAsAttr = Assert.Single(FindAttribute(lastParam.AttributeLists, "MarshalAs"));

        Assert.True(marshalAsAttr.ArgumentList?.Arguments[0].ToString() == "UnmanagedType.CustomMarshaler");
        Assert.Single(marshalAsAttr.ArgumentList?.Arguments.Where(arg => arg.ToString() == $"MarshalCookie = \"{WinRTClassName}\""));
        Assert.Single(marshalAsAttr.ArgumentList?.Arguments.Where(arg => arg.ToString() == $"MarshalType = \"{WinRTCustomMarshalerFullName}\""));

        // Make sure the WinRT marshaler was brought in
        Assert.Single(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void ComOutPtrTypedAsOutObject()
    {
        const string methodName = "CoCreateInstance";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedMethod(methodName), m => m.ParameterList.Parameters.Last() is { } last && last.Modifiers.Any(SyntaxKind.OutKeyword) && last.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ObjectKeyword } });
    }

    [Fact]
    public void AmbiguousApiName()
    {
        this.generator = this.CreateGenerator();
        Assert.False(this.generator.TryGenerate("IDENTITY_TYPE", out IReadOnlyList<string> preciseApi, CancellationToken.None));
        Assert.Equal(2, preciseApi.Count);
        Assert.Contains("Windows.Win32.NetworkManagement.NetworkPolicyServer.IDENTITY_TYPE", preciseApi);
        Assert.Contains("Windows.Win32.Security.Authentication.Identity.Provider.IDENTITY_TYPE", preciseApi);
    }

    [Fact]
    public void ReleaseMethodGeneratedWithHandleStruct()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("HANDLE", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.True(this.IsMethodGenerated("CloseHandle"));
    }

    [Theory]
    [InlineData("HANDLE")]
    [InlineData("HGDIOBJ")]
    public void HandleStructsHaveIsNullProperty(string handleName)
    {
        // A null HGDIOBJ has a specific meaning beyond just the concept of an invalid handle:
        // https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-selectobject#return-value
        this.AssertGeneratedMember(handleName, "IsNull", "internal bool IsNull => Value == default;");
    }

    [Fact]
    public void NamespaceHandleGetsNoSafeHandle()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreatePrivateNamespace", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Empty(this.FindGeneratedType("ClosePrivateNamespaceSafeHandle"));
    }

    [Fact]
    public void CreateFileUsesSafeHandles()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreateFile", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        Assert.Contains(
            this.FindGeneratedMethod("CreateFile"),
            createFileMethod => createFileMethod!.ReturnType.ToString() == "Microsoft.Win32.SafeHandles.SafeFileHandle"
                && createFileMethod.ParameterList.Parameters.Last().Type?.ToString() == "SafeHandle");
    }

    /// <summary>
    /// GetMessage should return BOOL rather than bool because it actually returns any of THREE values.
    /// </summary>
    [Fact]
    public void GetMessageW_ReturnsBOOL()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("GetMessage", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.All(this.FindGeneratedMethod("GetMessage"), method => Assert.True(method.ReturnType is QualifiedNameSyntax { Right: { Identifier: { ValueText: "BOOL" } } }));
    }

    [Theory, PairwiseData]
    public void NativeArray_OfManagedTypes_MarshaledAsLPArray(bool allowMarshaling)
    {
        const string ifaceName = "ID3D11DeviceContext";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var generatedMethod = this.FindGeneratedMethod("OMSetRenderTargets").Where(m => m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[0].Identifier.ValueText == "NumViews").FirstOrDefault();
        Assert.NotNull(generatedMethod);

        if (allowMarshaling)
        {
            Assert.Contains(generatedMethod!.ParameterList.Parameters[1].AttributeLists, al => IsAttributePresent(al, "MarshalAs"));
        }
        else
        {
            Assert.DoesNotContain(generatedMethod!.ParameterList.Parameters[1].AttributeLists, al => IsAttributePresent(al, "MarshalAs"));
        }
    }

    [Theory, PairwiseData]
    public void NativeArray_SizeParamIndex_ProducesSimplerFriendlyOverload(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("EvtNext", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        IEnumerable<MethodDeclarationSyntax> overloads = this.FindGeneratedMethod("EvtNext");
        Assert.NotEmpty(overloads.Where(o => o.ParameterList.Parameters.Count == 5 && (o.ParameterList.Parameters[1].Type?.ToString().StartsWith("Span<", StringComparison.Ordinal) ?? false)));
    }

    [Theory, PairwiseData]
    public void NonCOMInterfaceReferences(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        const string methodName = "D3DCompile"; // A method whose signature references non-COM interface ID3DInclude
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // The generated methods MUST reference the "interface" (which must actually be generated as a struct) by pointer.
        Assert.Contains(this.FindGeneratedType("ID3DInclude"), t => t is StructDeclarationSyntax);
        Assert.All(this.FindGeneratedMethod(methodName), m => Assert.True(m.ParameterList.Parameters[4].Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "ID3DInclude" } } } }));
    }

    [Theory, PairwiseData]
    public void BOOL_ReturnType_InCOMInterface(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("ISpellCheckerFactory", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        if (allowMarshaling)
        {
            Assert.Contains(this.FindGeneratedMethod("IsSupported"), method => method.ReturnType is QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "BOOL" } } });
        }
        else
        {
            Assert.Contains(this.FindGeneratedMethod("IsSupported"), method => method.ParameterList.Parameters.Last().Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "BOOL" } } } });
        }
    }

    [Theory, PairwiseData]
    public void GenerateByNamespace(bool correctCase)
    {
        this.generator = this.CreateGenerator();
        string ns = "Windows.Win32.Foundation";
        if (!correctCase)
        {
            ns = ns.ToUpperInvariant();
        }

        Assert.True(this.generator.TryGenerate(ns, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.NotEmpty(this.FindGeneratedType("BOOL"));
        Assert.NotEmpty(this.FindGeneratedType("SIZE"));
    }

    /// <summary>
    /// Verifies that fields are not converted from BOOL to bool.
    /// </summary>
    [Fact]
    public void BOOL_FieldRemainsBOOL()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("ICONINFO", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        var theStruct = (StructDeclarationSyntax)this.FindGeneratedType("ICONINFO").Single();
        VariableDeclarationSyntax field = theStruct.Members.OfType<FieldDeclarationSyntax>().Select(m => m.Declaration).Single(d => d.Variables.Any(v => v.Identifier.ValueText == "fIcon"));
        Assert.Equal("BOOL", Assert.IsType<QualifiedNameSyntax>(field.Type).Right.Identifier.ValueText);
    }

    [Theory, PairwiseData]
    public void BSTR_FieldsDoNotBecomeSafeHandles(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("DebugPropertyInfo", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structDecl = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("DebugPropertyInfo").Single());
        var bstrField = structDecl.Members.OfType<FieldDeclarationSyntax>().First(m => m.Declaration.Variables.Any(v => v.Identifier.ValueText == "m_bstrName"));
        Assert.Equal("BSTR", Assert.IsType<QualifiedNameSyntax>(bstrField.Declaration.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void TypeNameCollisionsDoNotCauseTooMuchCodeGen()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("TYPEDESC", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Empty(this.FindGeneratedType("D3DMATRIX"));
    }

    /// <summary>
    /// Verifies that MSIHANDLE is wrapped with a SafeHandle even though it is a 32-bit handle.
    /// This is safe because we never pass SafeHandle directly to extern methods, so we can fix the length of the parameter or return value.
    /// </summary>
    [Fact]
    public void MSIHANDLE_BecomesSafeHandle()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("MsiGetLastErrorRecord", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        Assert.Contains(
            this.FindGeneratedMethod("MsiGetLastErrorRecord"),
            method => method!.ReturnType is QualifiedNameSyntax { Right: { Identifier: { ValueText: "MSIHANDLE" } } });

        Assert.Contains(
            this.FindGeneratedMethod("MsiGetLastErrorRecord_SafeHandle"),
            method => method!.ReturnType?.ToString() == "MsiCloseHandleSafeHandle");

        MethodDeclarationSyntax releaseMethod = this.FindGeneratedMethod("MsiCloseHandle").Single();
        Assert.Equal("MSIHANDLE", Assert.IsType<QualifiedNameSyntax>(releaseMethod!.ParameterList.Parameters[0].Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void OutHandleParameterBecomesSafeHandle()
    {
        this.generator = this.CreateGenerator();
        const string methodName = "TcAddFilter";
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        Assert.Contains(
            this.FindGeneratedMethod(methodName),
            method => method!.ParameterList.Parameters[2].Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: nameof(Microsoft.Win32.SafeHandles.SafeFileHandle) } } });

        Assert.Contains(
            this.FindGeneratedMethod(methodName),
            method => method!.ParameterList.Parameters[0].Type is IdentifierNameSyntax { Identifier: { ValueText: nameof(SafeHandle) } });
    }

    [Fact]
    public void Const_PWSTR_Becomes_PCWSTR_and_String()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("StrCmpLogical", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        bool foundPCWSTROverload = false;
        bool foundStringOverload = false;
        IEnumerable<MethodDeclarationSyntax> overloads = this.FindGeneratedMethod("StrCmpLogical");
        foreach (MethodDeclarationSyntax method in overloads)
        {
            foundPCWSTROverload |= method!.ParameterList.Parameters[0].Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } };
            foundStringOverload |= method!.ParameterList.Parameters[0].Type?.ToString() == "string";
        }

        Assert.True(foundPCWSTROverload, "PCWSTR overload is missing.");
        Assert.True(foundStringOverload, "string overload is missing.");
        Assert.Equal(2, overloads.Count());
    }

    ////[Fact]
    ////public void CrossWinmdTypeReference()
    ////{
    ////    this.generator = this.CreateGenerator();
    ////    using Generator diaGenerator = this.CreateGenerator(DiaMetadataPath);
    ////    var super = SuperGenerator.Combine(this.generator, diaGenerator);
    ////    Assert.True(diaGenerator.TryGenerate("E_PDB_NOT_FOUND", CancellationToken.None));
    ////    this.CollectGeneratedCode(this.generator);
    ////    this.CollectGeneratedCode(diaGenerator);
    ////    this.AssertNoDiagnostics();

    ////    Assert.Single(this.FindGeneratedType("HRESULT"));
    ////    Assert.Single(this.FindGeneratedConstant("E_PDB_NOT_FOUND"));
    ////}

    [Theory, CombinatorialData]
    public void ArchitectureSpecificAPIsTreatment(
        [CombinatorialValues("MEMORY_BASIC_INFORMATION", "SP_PROPCHANGE_PARAMS", "JsCreateContext", "IShellBrowser")] string apiName,
        [CombinatorialValues(Platform.AnyCpu, Platform.X64, Platform.X86)] Platform platform,
        bool allowMarshaling)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        if (platform == Platform.AnyCpu)
        {
            // AnyCPU targets should throw an exception with a helpful error message when asked for arch-specific APIs
            var ex = Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate(apiName, CancellationToken.None));
            this.logger.WriteLine(ex.Message);
            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics();
        }
        else
        {
            // Arch-specific compilations should generate the requested APIs.
            Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics();
        }
    }

    [Fact]
    public void MultipleEntrypointsToOmittedArchSpecificApis()
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.AnyCpu));
        this.generator = this.CreateGenerator();

        // Request a struct that depends on arch-specific IP6_ADDRESS.
        Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate("DNS_SERVICE_INSTANCE", CancellationToken.None));

        // Request a struct that depends on DNS_SERVICE_INSTANCE.
        Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate("DNS_SERVICE_REGISTER_REQUEST", CancellationToken.None));

        // Verify that no uncompilable code was generated.
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, CombinatorialData]
    public void TypeRefsToArchSpecificApis(
        [CombinatorialValues(Platform.X64, Platform.X86)] Platform platform)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator();

        // Request a struct directly, and indirectly through another that references it.
        // This verifies that even if the metadata references a particular arch of the structure,
        // the right one for the CPU architecture is generated.
        Assert.True(this.generator.TryGenerate("SP_PROPCHANGE_PARAMS", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("SP_CLASSINSTALL_HEADER", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Verify that [Pack = 1] appears on both structures.
        foreach (string structName in new[] { "SP_CLASSINSTALL_HEADER", "SP_PROPCHANGE_PARAMS" })
        {
            var header = this.FindGeneratedType(structName).Single();
            SeparatedSyntaxList<AttributeArgumentSyntax> attributes = header.AttributeLists.SelectMany(al => al.Attributes).FirstOrDefault(att => att.Name is IdentifierNameSyntax { Identifier: { ValueText: "StructLayout" } })?.ArgumentList?.Arguments ?? default;
            Predicate<AttributeArgumentSyntax> matchPredicate = att => att.NameEquals is { Name: { Identifier: { ValueText: "Pack" } } };
            if (platform == Platform.X86)
            {
                Assert.Contains(attributes, matchPredicate);
            }
            else
            {
                Assert.DoesNotContain(attributes, matchPredicate);
            }
        }
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("HRESULT")]
    [InlineData("MEMORY_BASIC_INFORMATION")]
    public void StructsArePartial(string structName)
    {
        this.compilation = this.starterCompilations["net5.0-x64"]; // MEMORY_BASIC_INFORMATION is arch-specific
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structDecl = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(structName).Single());
        Assert.True(structDecl.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    [Fact]
    public void PartialStructsAllowUserContributions()
    {
        const string structName = "HRESULT";
        this.compilation = this.compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.Windows.Sdk { partial struct HRESULT { void Foo() { } } }", this.parseOptions, "myHRESULT.cs"));

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        bool hasFooMethod = false;
        bool hasValueProperty = false;
        foreach (StructDeclarationSyntax structDecl in this.FindGeneratedType(structName))
        {
            hasFooMethod |= structDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "Foo");
            hasValueProperty |= structDecl.Members.OfType<FieldDeclarationSyntax>().Any(p => p.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText == "Value");
        }

        Assert.True(hasFooMethod, "User-defined method not found.");
        Assert.True(hasValueProperty, "Projected members not found.");
    }

    [Fact]
    public void PROC_GeneratedAsStruct()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("PROC", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("PROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void FARPROC_GeneratedAsStruct(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("FARPROC", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("FARPROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Theory, PairwiseData]
    public void FARPROC_AsFieldType(bool allowMarshaling)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate("EXTPUSH", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        StructDeclarationSyntax type = Assert.IsType<StructDeclarationSyntax>(Assert.Single(this.FindGeneratedType("_Anonymous1_e__Union")));
        var callback = (FieldDeclarationSyntax)type.Members[1];
        Assert.True(callback.Declaration.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "FARPROC" } } }, "Field type was " + callback.Declaration.Type);
    }

    [Fact]
    public void GetLastErrorGenerationThrowsWhenExplicitlyCalled()
    {
        this.generator = this.CreateGenerator();
        Assert.Throws<NotSupportedException>(() => this.generator.TryGenerate("GetLastError", CancellationToken.None));
    }

    [Fact(Skip = "https://github.com/microsoft/win32metadata/issues/129")]
    public void DeleteObject_TakesTypeDefStruct()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("DeleteObject", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        MethodDeclarationSyntax? deleteObjectMethod = this.FindGeneratedMethod("DeleteObject").FirstOrDefault();
        Assert.NotNull(deleteObjectMethod);
        Assert.Equal("HGDIOBJ", Assert.IsType<IdentifierNameSyntax>(deleteObjectMethod!.ParameterList.Parameters[0].Type).Identifier.ValueText);
    }

    [Fact]
    public void CollidingStructNotGenerated()
    {
        const string test = @"
namespace Microsoft.Windows.Sdk
{
    internal enum FILE_CREATE_FLAGS
    {
        CREATE_NEW = 1,
        CREATE_ALWAYS = 2,
        OPEN_EXISTING = 3,
        OPEN_ALWAYS = 4,
        TRUNCATE_EXISTING = 5,
    }
}
";
        this.compilation = this.compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(test, path: "test.cs"));
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreateFile", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("HRESULT")]
    [InlineData("NTSTATUS")]
    [InlineData("PCSTR")]
    [InlineData("PCWSTR")]
    [InlineData("PWSTR")]
    public void SynthesizedTypesCanBeDirectlyRequested(string synthesizedTypeName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(synthesizedTypeName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType(synthesizedTypeName));
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("HRESULT")]
    [InlineData("NTSTATUS")]
    [InlineData("PCSTR")]
    [InlineData("PCWSTR")]
    [InlineData("PWSTR")]
    public async Task SynthesizedTypesWorkInNet35(string synthesizedTypeName)
    {
        this.compilation = await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net35);
        this.generator = this.CreateGenerator();

        Assert.True(this.generator.TryGenerate(synthesizedTypeName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType(synthesizedTypeName));
    }

    /// <summary>
    /// Validates that where MemoryMarshal.CreateSpan isn't available, a substitute indexer is offered.
    /// </summary>
    [Fact]
    public void FixedLengthInlineArraysOfferExtensionIndexerWhereNoSpanPossible()
    {
        const string expected = @"		internal partial struct MainAVIHeader
		{
			internal uint dwMicroSecPerFrame;
			internal uint dwMaxBytesPerSec;
			internal uint dwPaddingGranularity;
			internal uint dwFlags;
			internal uint dwTotalFrames;
			internal uint dwInitialFrames;
			internal uint dwStreams;
			internal uint dwSuggestedBufferSize;
			internal uint dwWidth;
			internal uint dwHeight;
			internal __uint_4 dwReserved;

			internal partial struct __uint_4
			{
				internal uint _0,_1,_2,_3;

				/// <summary>Always <c>4</c>.</summary>
				internal readonly int Length => 4;

				internal unsafe readonly void CopyTo(Span<uint> target, int length = 4)
				{
					if (length > 4)throw new ArgumentOutOfRangeException(""length"");
					fixed (uint* p0 = &_0)
for(int i = 0;
i < length;
i++)						target[i]= p0[i];
				}

				internal unsafe readonly uint[] ToArray(int length = 4)
				{
					if (length > 4)throw new ArgumentOutOfRangeException(""length"");
					uint[] target = new uint[length];
					fixed (uint* p0 = &_0)
for(int i = 0;
i < length;
i++)						target[i]= p0[i];
					return target;
				}

				internal unsafe readonly bool Equals(ReadOnlySpan<uint> value)
				{
					fixed (uint* p0 = &_0)
					{
 						int commonLength = Math.Min(value.Length, 4);
for(int i = 0;
i < commonLength;
i++)						if (p0[i] != value[i])							return false;
for(int i = commonLength;
i < 4;
i++)						if (p0[i] != default(uint))							return false;
					}
					return true;
				}
			}
		}
";

        const string expectedIndexer = @"
	internal static partial class InlineArrayIndexerExtensions
	{
		internal static unsafe ref readonly uint ReadOnlyItemRef(this in win32.Graphics.DirectShow.MainAVIHeader.__uint_4 @this, int index)
		{
			fixed (uint* p0 = &@this._0)
				return ref p0[index];
		}

		internal static unsafe ref uint ItemRef(this ref win32.Graphics.DirectShow.MainAVIHeader.__uint_4 @this, int index)
		{
			fixed (uint* p0 = &@this._0)
				return ref p0[index];
		}
	}
";

        this.AssertGeneratedType("MainAVIHeader", expected, expectedIndexer);
    }

    /// <summary>
    /// Validates that where MemoryMarshal.CreateSpan is available, a <see cref="Span{T}"/> method and proper indexer is offered.
    /// </summary>
    [Fact]
    public void FixedLengthInlineArraysGetSpanWherePossible()
    {
        const string expected = @"		internal partial struct MainAVIHeader
		{
			internal uint dwMicroSecPerFrame;
			internal uint dwMaxBytesPerSec;
			internal uint dwPaddingGranularity;
			internal uint dwFlags;
			internal uint dwTotalFrames;
			internal uint dwInitialFrames;
			internal uint dwStreams;
			internal uint dwSuggestedBufferSize;
			internal uint dwWidth;
			internal uint dwHeight;
			internal __uint_4 dwReserved;

			internal partial struct __uint_4
			{
				internal uint _0,_1,_2,_3;

				/// <summary>Always <c>4</c>.</summary>
				internal readonly int Length => 4;

				/// <summary>
				/// Gets a ref to an individual element of the inline array.
				/// ⚠ Important ⚠: When this struct is on the stack, do not let the returned reference outlive the stack frame that defines it.
				/// </summary>
				internal ref uint this[int index] => ref AsSpan()[index];

				/// <summary>
				/// Gets this inline array as a span.
				/// </summary>
				/// <remarks>
				/// ⚠ Important ⚠: When this struct is on the stack, do not let the returned span outlive the stack frame that defines it.
				/// </remarks>
				internal Span<uint> AsSpan() => MemoryMarshal.CreateSpan(ref _0, 4);

				internal unsafe readonly void CopyTo(Span<uint> target, int length = 4)
				{
					if (length > 4)throw new ArgumentOutOfRangeException(""length"");
					fixed (uint* p0 = &_0)
for(int i = 0;
i < length;
i++)						target[i]= p0[i];
				}

				internal unsafe readonly uint[] ToArray(int length = 4)
				{
					if (length > 4)throw new ArgumentOutOfRangeException(""length"");
					uint[] target = new uint[length];
					fixed (uint* p0 = &_0)
for(int i = 0;
i < length;
i++)						target[i]= p0[i];
					return target;
				}

				internal unsafe readonly bool Equals(ReadOnlySpan<uint> value)
				{
					fixed (uint* p0 = &_0)
					{
 						int commonLength = Math.Min(value.Length, 4);
for(int i = 0;
i < commonLength;
i++)						if (p0[i] != value[i])							return false;
for(int i = commonLength;
i < 4;
i++)						if (p0[i] != default(uint))							return false;
					}
					return true;
				}
			}
		}
";

        const string expectedIndexer = @"
	internal static partial class InlineArrayIndexerExtensions
	{
		internal static unsafe ref readonly uint ReadOnlyItemRef(this in win32.Graphics.DirectShow.MainAVIHeader.__uint_4 @this, int index)
		{
			fixed (uint* p0 = &@this._0)
				return ref p0[index];
		}

		internal static unsafe ref uint ItemRef(this ref win32.Graphics.DirectShow.MainAVIHeader.__uint_4 @this, int index)
		{
			fixed (uint* p0 = &@this._0)
				return ref p0[index];
		}
	}
";

        this.compilation = this.starterCompilations["net5.0"];
        this.AssertGeneratedType("MainAVIHeader", expected, expectedIndexer);
    }

    [Fact]
    public void NullMethodsClass()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { MethodsClassName = null });
        Assert.True(this.generator.TryGenerate("GetTickCount", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType("Kernel32"));
        Assert.Empty(this.FindGeneratedType("PInvoke"));
    }

    [Fact]
    public void RenamedMethodsClass()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { MethodsClassName = "MyPInvoke" });
        Assert.True(this.generator.TryGenerate("GetTickCount", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        Assert.Single(this.FindGeneratedType("MyPInvoke"));
        Assert.Empty(this.FindGeneratedType("PInvoke"));
    }

    [Fact]
    public void RenamedConstantsClass()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { ConstantsClassName = "MyConstants" });
        Assert.True(this.generator.TryGenerate("CDB_REPORT_BITS", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType("MyConstants"));
        Assert.Empty(this.FindGeneratedType("Constants"));
    }

    [Theory, PairwiseData]
    public void FullGeneration(bool allowMarshaling, [CombinatorialValues(Platform.AnyCpu, Platform.X86, Platform.X64, Platform.Arm64)] Platform platform)
    {
        var generatorOptions = new GeneratorOptions { AllowMarshaling = allowMarshaling };
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator(generatorOptions);
        this.generator.GenerateAll(CancellationToken.None);
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics(logGeneratedCode: false);
    }

    [Theory, PairwiseData]
    public void ProjectReferenceBetweenTwoGeneratingProjects(bool internalsVisibleTo)
    {
        CSharpCompilation referencedProject = this.compilation
            .WithAssemblyName("refdProj");
        if (internalsVisibleTo)
        {
            referencedProject = referencedProject.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText($@"[assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute(""{this.compilation.AssemblyName}"")]", this.parseOptions));
        }

        using var referencedGenerator = this.CreateGenerator(new GeneratorOptions { MethodsClassName = "P1" }, referencedProject);
        Assert.True(referencedGenerator.TryGenerate("LockWorkStation", CancellationToken.None));
        Assert.True(referencedGenerator.TryGenerate("CreateFile", CancellationToken.None));
        referencedProject = this.AddGeneratedCode(referencedProject, referencedGenerator);
        this.AssertNoDiagnostics(referencedProject);

        // Now produce more code in a referencing project that includes at least one of the same types as generated in the referenced project.
        this.compilation = this.compilation.AddReferences(referencedProject.ToMetadataReference());
        this.generator = this.CreateGenerator(new GeneratorOptions { MethodsClassName = "P2" });
        Assert.True(this.generator.TryGenerate("HidD_GetAttributes", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public async Task TestSimpleStructure()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "BOOL"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.BOOL.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		internal readonly partial struct BOOL
		{
			private readonly int value;

			internal int Value => this.value;
			internal unsafe BOOL(bool value) => this.value = *(sbyte*)&value;
			internal BOOL(int value) => this.value = value;
			public static unsafe implicit operator bool(BOOL value)
			{
				sbyte v = checked((sbyte)value.value);
				return *(bool*)&v;
			}
			public static implicit operator BOOL(bool value) => new BOOL(value);
			public static explicit operator BOOL(int value) => new BOOL(value);
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestSimpleEnum()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "DISPLAYCONFIG_SCANLINE_ORDERING"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.DISPLAYCONFIG_SCANLINE_ORDERING.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace UI.DisplayDevices
	{
		/// <summary>The DISPLAYCONFIG_SCANLINE_ORDERING enumeration specifies the method that the display uses to create an image on a screen.</summary>
		/// <remarks>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//wingdi/ne-wingdi-displayconfig_scanline_ordering"">Learn more about this API from docs.microsoft.com</see>.</para>
		/// </remarks>
		internal enum DISPLAYCONFIG_SCANLINE_ORDERING
		{
			/// <summary>Indicates that scan-line ordering of the output is unspecified. The caller can only set the <b>scanLineOrdering</b> member of the <a href=""https://docs.microsoft.com/windows/desktop/api/wingdi/ns-wingdi-displayconfig_path_target_info"">DISPLAYCONFIG_PATH_TARGET_INFO</a> structure in a call to the <a href=""https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-setdisplayconfig"">SetDisplayConfig</a> function to DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED if the caller also set the refresh rate denominator and numerator of the <b>refreshRate</b> member both to zero. In this case, <b>SetDisplayConfig</b> uses the best refresh rate it can find.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
			/// <summary>Indicates that the output is a progressive image.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
			/// <summary>Indicates that the output is an interlaced image that is created beginning with the upper field.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
			/// <summary>Indicates that the output is an interlaced image that is created beginning with the upper field.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = 2,
			/// <summary>Indicates that the output is an interlaced image that is created beginning with the lower field.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3,
			/// <summary>Forces this enumeration to compile to 32 bits in size. Without this value, some compilers would allow this enumeration to compile to a size other than 32 bits. You should not use this value.</summary>
			DISPLAYCONFIG_SCANLINE_ORDERING_FORCE_UINT32 = -1,
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestSimpleEnumWithoutDocs()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "DISPLAYCONFIG_SCANLINE_ORDERING"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString(omitDocs: true)),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.DISPLAYCONFIG_SCANLINE_ORDERING.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace UI.DisplayDevices
	{
		internal enum DISPLAYCONFIG_SCANLINE_ORDERING
		{
			DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
			DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = 2,
			DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3,
			DISPLAYCONFIG_SCANLINE_ORDERING_FORCE_UINT32 = -1,
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestFlagsEnum()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "FILE_ACCESS_FLAGS"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.FILE_ACCESS_FLAGS.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		[Flags]
		internal enum FILE_ACCESS_FLAGS : uint
		{
			FILE_READ_DATA = 0x00000001,
			FILE_LIST_DIRECTORY = 0x00000001,
			FILE_WRITE_DATA = 0x00000002,
			FILE_ADD_FILE = 0x00000002,
			FILE_APPEND_DATA = 0x00000004,
			FILE_ADD_SUBDIRECTORY = 0x00000004,
			FILE_CREATE_PIPE_INSTANCE = 0x00000004,
			FILE_READ_EA = 0x00000008,
			FILE_WRITE_EA = 0x00000010,
			FILE_EXECUTE = 0x00000020,
			FILE_TRAVERSE = 0x00000020,
			FILE_DELETE_CHILD = 0x00000040,
			FILE_READ_ATTRIBUTES = 0x00000080,
			FILE_WRITE_ATTRIBUTES = 0x00000100,
			READ_CONTROL = 0x00020000,
			SYNCHRONIZE = 0x00100000,
			STANDARD_RIGHTS_REQUIRED = 0x000F0000,
			STANDARD_RIGHTS_READ = 0x00020000,
			STANDARD_RIGHTS_WRITE = 0x00020000,
			STANDARD_RIGHTS_EXECUTE = 0x00020000,
			STANDARD_RIGHTS_ALL = 0x001F0000,
			SPECIFIC_RIGHTS_ALL = 0x0000FFFF,
			FILE_ALL_ACCESS = 0x001F01FF,
			FILE_GENERIC_READ = 0x00120089,
			FILE_GENERIC_WRITE = 0x00120116,
			FILE_GENERIC_EXECUTE = 0x001200A0,
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestSimpleDelegate()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "WNDENUMPROC"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.BOOL.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		internal readonly partial struct BOOL
		{
			private readonly int value;

			internal int Value => this.value;
			internal unsafe BOOL(bool value) => this.value = *(sbyte*)&value;
			internal BOOL(int value) => this.value = value;
			public static unsafe implicit operator bool(BOOL value)
			{
				sbyte v = checked((sbyte)value.value);
				return *(bool*)&v;
			}
			public static implicit operator BOOL(bool value) => new BOOL(value);
			public static explicit operator BOOL(int value) => new BOOL(value);
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.Delegates.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace UI.WindowsAndMessaging
	{
		[UnmanagedFunctionPointerAttribute(CallingConvention.Winapi)]
		internal unsafe delegate win32.Foundation.BOOL WNDENUMPROC(win32.Foundation.HWND param0, win32.Foundation.LPARAM param1);
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.HWND.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		[DebuggerDisplay(""{Value}"")]
		internal readonly partial struct HWND
			: IEquatable<HWND>
		{
			internal readonly nint Value;
			internal HWND(nint value) => this.Value = value;
			public static implicit operator nint(HWND value) => value.Value;
			public static explicit operator HWND(nint value) => new HWND(value);
			public static bool operator ==(HWND left, HWND right) => left.Value == right.Value;
			public static bool operator !=(HWND left, HWND right) => !(left == right);

			public bool Equals(HWND other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is HWND other && this.Equals(other);

			public override int GetHashCode() => this.Value.GetHashCode();
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.LPARAM.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		[DebuggerDisplay(""{Value}"")]
		internal readonly partial struct LPARAM
			: IEquatable<LPARAM>
		{
			internal readonly nint Value;
			internal LPARAM(nint value) => this.Value = value;
			public static implicit operator nint(LPARAM value) => value.Value;
			public static implicit operator LPARAM(nint value) => new LPARAM(value);
			public static bool operator ==(LPARAM left, LPARAM right) => left.Value == right.Value;
			public static bool operator !=(LPARAM left, LPARAM right) => !(left == right);

			public bool Equals(LPARAM other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is LPARAM other && this.Equals(other);

			public override int GetHashCode() => this.Value.GetHashCode();
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestSimpleMethod()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "ReleaseDC"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.HDC.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Graphics.Gdi
	{
		[DebuggerDisplay(""{Value}"")]
		internal readonly partial struct HDC
			: IEquatable<HDC>
		{
			internal readonly IntPtr Value;
			internal HDC(IntPtr value) => this.Value = value;

			internal bool IsNull => Value == default;
			public static implicit operator IntPtr(HDC value) => value.Value;
			public static explicit operator HDC(IntPtr value) => new HDC(value);
			public static bool operator ==(HDC left, HDC right) => left.Value == right.Value;
			public static bool operator !=(HDC left, HDC right) => !(left == right);

			public bool Equals(HDC other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is HDC other && this.Equals(other);

			public override int GetHashCode() => this.Value.GetHashCode();
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.HWND.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		[DebuggerDisplay(""{Value}"")]
		internal readonly partial struct HWND
			: IEquatable<HWND>
		{
			internal readonly nint Value;
			internal HWND(nint value) => this.Value = value;
			public static implicit operator nint(HWND value) => value.Value;
			public static explicit operator HWND(nint value) => new HWND(value);
			public static bool operator ==(HWND left, HWND right) => left.Value == right.Value;
			public static bool operator !=(HWND left, HWND right) => !(left == right);

			public bool Equals(HWND other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is HWND other && this.Equals(other);

			public override int GetHashCode() => this.Value.GetHashCode();
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.PInvoke.User32.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;


	/// <content>
	/// Contains extern methods from ""User32.dll"".
	/// </content>
	internal static partial class PInvoke
	{
		/// <summary>The ReleaseDC function releases a device context (DC), freeing it for use by other applications. The effect of the ReleaseDC function depends on the type of DC. It frees only common and window DCs. It has no effect on class or private DCs.</summary>
		/// <param name=""hWnd"">A handle to the window whose DC is to be released.</param>
		/// <param name=""hDC"">A handle to the DC to be released.</param>
		/// <returns>
		/// <para>The return value indicates whether the DC was released. If the DC was released, the return value is 1. If the DC was not released, the return value is zero.</para>
		/// </returns>
		/// <remarks>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//winuser/nf-winuser-releasedc"">Learn more about this API from docs.microsoft.com</see>.</para>
		/// </remarks>
		[DllImport(""User32"", ExactSpelling = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static extern int ReleaseDC(win32.Foundation.HWND hWnd, win32.Graphics.Gdi.HDC hDC);
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task TestMethodWithOverloads()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "CreateFile"),
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    (typeof(SourceGenerator), "Windows.Win32.BOOL.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		internal readonly partial struct BOOL
		{
			private readonly int value;

			internal int Value => this.value;
			internal unsafe BOOL(bool value) => this.value = *(sbyte*)&value;
			internal BOOL(int value) => this.value = value;
			public static unsafe implicit operator bool(BOOL value)
			{
				sbyte v = checked((sbyte)value.value);
				return *(bool*)&v;
			}
			public static implicit operator BOOL(bool value) => new BOOL(value);
			public static explicit operator BOOL(int value) => new BOOL(value);
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.FILE_ACCESS_FLAGS.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		[Flags]
		internal enum FILE_ACCESS_FLAGS : uint
		{
			FILE_READ_DATA = 0x00000001,
			FILE_LIST_DIRECTORY = 0x00000001,
			FILE_WRITE_DATA = 0x00000002,
			FILE_ADD_FILE = 0x00000002,
			FILE_APPEND_DATA = 0x00000004,
			FILE_ADD_SUBDIRECTORY = 0x00000004,
			FILE_CREATE_PIPE_INSTANCE = 0x00000004,
			FILE_READ_EA = 0x00000008,
			FILE_WRITE_EA = 0x00000010,
			FILE_EXECUTE = 0x00000020,
			FILE_TRAVERSE = 0x00000020,
			FILE_DELETE_CHILD = 0x00000040,
			FILE_READ_ATTRIBUTES = 0x00000080,
			FILE_WRITE_ATTRIBUTES = 0x00000100,
			READ_CONTROL = 0x00020000,
			SYNCHRONIZE = 0x00100000,
			STANDARD_RIGHTS_REQUIRED = 0x000F0000,
			STANDARD_RIGHTS_READ = 0x00020000,
			STANDARD_RIGHTS_WRITE = 0x00020000,
			STANDARD_RIGHTS_EXECUTE = 0x00020000,
			STANDARD_RIGHTS_ALL = 0x001F0000,
			SPECIFIC_RIGHTS_ALL = 0x0000FFFF,
			FILE_ALL_ACCESS = 0x001F01FF,
			FILE_GENERIC_READ = 0x00120089,
			FILE_GENERIC_WRITE = 0x00120116,
			FILE_GENERIC_EXECUTE = 0x001200A0,
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.FILE_CREATION_DISPOSITION.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		internal enum FILE_CREATION_DISPOSITION : uint
		{
			CREATE_NEW = 1U,
			CREATE_ALWAYS = 2U,
			OPEN_EXISTING = 3U,
			OPEN_ALWAYS = 4U,
			TRUNCATE_EXISTING = 5U,
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.FILE_FLAGS_AND_ATTRIBUTES.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		[Flags]
		internal enum FILE_FLAGS_AND_ATTRIBUTES : uint
		{
			FILE_ATTRIBUTE_READONLY = 0x00000001,
			FILE_ATTRIBUTE_HIDDEN = 0x00000002,
			FILE_ATTRIBUTE_SYSTEM = 0x00000004,
			FILE_ATTRIBUTE_DIRECTORY = 0x00000010,
			FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
			FILE_ATTRIBUTE_DEVICE = 0x00000040,
			FILE_ATTRIBUTE_NORMAL = 0x00000080,
			FILE_ATTRIBUTE_TEMPORARY = 0x00000100,
			FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,
			FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,
			FILE_ATTRIBUTE_COMPRESSED = 0x00000800,
			FILE_ATTRIBUTE_OFFLINE = 0x00001000,
			FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
			FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
			FILE_ATTRIBUTE_INTEGRITY_STREAM = 0x00008000,
			FILE_ATTRIBUTE_VIRTUAL = 0x00010000,
			FILE_ATTRIBUTE_NO_SCRUB_DATA = 0x00020000,
			FILE_ATTRIBUTE_EA = 0x00040000,
			FILE_ATTRIBUTE_PINNED = 0x00080000,
			FILE_ATTRIBUTE_UNPINNED = 0x00100000,
			FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000,
			FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000,
			FILE_FLAG_WRITE_THROUGH = 0x80000000,
			FILE_FLAG_OVERLAPPED = 0x40000000,
			FILE_FLAG_NO_BUFFERING = 0x20000000,
			FILE_FLAG_RANDOM_ACCESS = 0x10000000,
			FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
			FILE_FLAG_DELETE_ON_CLOSE = 0x04000000,
			FILE_FLAG_BACKUP_SEMANTICS = 0x02000000,
			FILE_FLAG_POSIX_SEMANTICS = 0x01000000,
			FILE_FLAG_SESSION_AWARE = 0x00800000,
			FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000,
			FILE_FLAG_OPEN_NO_RECALL = 0x00100000,
			FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000,
			PIPE_ACCESS_DUPLEX = 0x00000003,
			PIPE_ACCESS_INBOUND = 0x00000001,
			PIPE_ACCESS_OUTBOUND = 0x00000002,
			SECURITY_ANONYMOUS = 0x00000000,
			SECURITY_IDENTIFICATION = 0x00010000,
			SECURITY_IMPERSONATION = 0x00020000,
			SECURITY_DELEGATION = 0x00030000,
			SECURITY_CONTEXT_TRACKING = 0x00040000,
			SECURITY_EFFECTIVE_ONLY = 0x00080000,
			SECURITY_SQOS_PRESENT = 0x00100000,
			SECURITY_VALID_SQOS_FLAGS = 0x001F0000,
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.FILE_SHARE_MODE.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		[Flags]
		internal enum FILE_SHARE_MODE : uint
		{
			FILE_SHARE_NONE = 0x00000000,
			FILE_SHARE_DELETE = 0x00000004,
			FILE_SHARE_READ = 0x00000001,
			FILE_SHARE_WRITE = 0x00000002,
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.HANDLE.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		[DebuggerDisplay(""{Value}"")]
		internal readonly partial struct HANDLE
			: IEquatable<HANDLE>
		{
			internal readonly IntPtr Value;
			internal HANDLE(IntPtr value) => this.Value = value;

			internal bool IsNull => Value == default;
			public static implicit operator IntPtr(HANDLE value) => value.Value;
			public static explicit operator HANDLE(IntPtr value) => new HANDLE(value);
			public static bool operator ==(HANDLE left, HANDLE right) => left.Value == right.Value;
			public static bool operator !=(HANDLE left, HANDLE right) => !(left == right);

			public bool Equals(HANDLE other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is HANDLE other && this.Equals(other);

			public override int GetHashCode() => this.Value.GetHashCode();
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.PCWSTR.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
			/// <summary>
			/// A pointer to a constant character string.
			/// </summary>
		[DebuggerDisplay(""{"" + nameof(DebuggerDisplay) + ""}"")]
		internal unsafe readonly partial struct PCWSTR
			: IEquatable<PCWSTR>
		{
			/// <summary>
			/// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
			/// </summary>
			internal readonly char* Value;
			internal PCWSTR(char* value) => this.Value = value;
			public static explicit operator char*(PCWSTR value) => value.Value;
			public static implicit operator PCWSTR(char* value) => new PCWSTR(value);
			public static implicit operator PCWSTR(PWSTR value) => new PCWSTR(value.Value);

			public bool Equals(PCWSTR other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is PCWSTR other && this.Equals(other);

			public override int GetHashCode() => unchecked((int)this.Value);


			/// <summary>
			/// Gets the number of characters up to the first null character (exclusive).
			/// </summary>
			internal int Length
			{
				get
				{
					char* p = this.Value;
					if (p is null)
						return 0;
					while (*p != '\0')
						p++;
					return checked((int)(p - this.Value));
				}
			}


			/// <summary>
			/// Returns a <see langword=""string""/> with a copy of this character array.
			/// </summary>
			/// <returns>A <see langword=""string""/>, or <see langword=""null""/> if <see cref=""Value""/> is <see langword=""null""/>.</returns>
			public override string ToString() => this.Value is null ? null : new string(this.Value);


			private string DebuggerDisplay => this.ToString();

			internal ReadOnlySpan<char> AsSpan() => this.Value is null ? default(ReadOnlySpan<char>) : new ReadOnlySpan<char>(this.Value, this.Length);
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.PInvoke.Kernel32.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;


	/// <content>
	/// Contains extern methods from ""Kernel32.dll"".
	/// </content>
	internal static partial class PInvoke
	{
		/// <summary>Closes an open object handle.</summary>
		/// <param name=""hObject"">A valid handle to an open object.</param>
		/// <returns>
		/// <para>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero. To get extended error information, call <a href=""/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror"">GetLastError</a>. If the application is running under a debugger,  the function will throw an exception if it receives either a  handle value that is not valid  or a pseudo-handle value. This can happen if you close a handle twice, or if you  call <b>CloseHandle</b> on a handle returned by the <a href=""/windows/desktop/api/fileapi/nf-fileapi-findfirstfilea"">FindFirstFile</a> function instead of calling the <a href=""/windows/desktop/api/fileapi/nf-fileapi-findclose"">FindClose</a> function.</para>
		/// </returns>
		/// <remarks>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//handleapi/nf-handleapi-closehandle"">Learn more about this API from docs.microsoft.com</see>.</para>
		/// </remarks>
		[DllImport(""Kernel32"", ExactSpelling = true, SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static extern win32.Foundation.BOOL CloseHandle(win32.Foundation.HANDLE hObject);

		/// <inheritdoc cref=""CreateFile(win32.Foundation.PCWSTR, win32.Storage.FileSystem.FILE_ACCESS_FLAGS, win32.Storage.FileSystem.FILE_SHARE_MODE, win32.Security.SECURITY_ATTRIBUTES*, win32.Storage.FileSystem.FILE_CREATION_DISPOSITION, win32.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES, win32.Foundation.HANDLE)""/>
		internal static unsafe Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string lpFileName, win32.Storage.FileSystem.FILE_ACCESS_FLAGS dwDesiredAccess, win32.Storage.FileSystem.FILE_SHARE_MODE dwShareMode, win32.Security.SECURITY_ATTRIBUTES? lpSecurityAttributes, win32.Storage.FileSystem.FILE_CREATION_DISPOSITION dwCreationDisposition, win32.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES dwFlagsAndAttributes, SafeHandle hTemplateFile)
		{
			bool hTemplateFileAddRef = false;
			try
			{
				fixed (char* lpFileNameLocal = lpFileName)
				{
					win32.Security.SECURITY_ATTRIBUTES lpSecurityAttributesLocal = lpSecurityAttributes.HasValue ? lpSecurityAttributes.Value : default(win32.Security.SECURITY_ATTRIBUTES);
					win32.Foundation.HANDLE hTemplateFileLocal;
					if (hTemplateFile is object)
					{
						hTemplateFile.DangerousAddRef(ref hTemplateFileAddRef);
						hTemplateFileLocal = (win32.Foundation.HANDLE)hTemplateFile.DangerousGetHandle();
					}
					else
						hTemplateFileLocal = default(win32.Foundation.HANDLE);
					win32.Foundation.HANDLE __result = PInvoke.CreateFile(lpFileNameLocal, dwDesiredAccess, dwShareMode, lpSecurityAttributes.HasValue ? &lpSecurityAttributesLocal : null, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFileLocal);
					return new Microsoft.Win32.SafeHandles.SafeFileHandle(__result, ownsHandle: true);
				}
			}
			finally
			{
				if (hTemplateFileAddRef)
					hTemplateFile.DangerousRelease();
			}
		}

		/// <summary>Creates or opens a file or I/O device. The most commonly used I/O devices are as follows:\_file, file stream, directory, physical disk, volume, console buffer, tape drive, communications resource, mailslot, and pipe.</summary>
		/// <param name=""lpFileName"">
		/// <para>The name of the file or device to be created or opened. You may use either forward slashes (/) or backslashes (\\) in this name. In the ANSI version of this function, the name is limited to <b>MAX_PATH</b> characters. To extend this limit to 32,767 wide characters, use this Unicode version of the function and prepend ""\\\\?\\"" to the path. For more information, see <a href=""https://docs.microsoft.com/windows/desktop/FileIO/naming-a-file"">Naming Files, Paths, and Namespaces</a>. For information on special device names, see <a href=""https://docs.microsoft.com/windows/desktop/FileIO/defining-an-ms-dos-device-name"">Defining an MS-DOS Device Name</a>. To create a file stream, specify the name of the file, a colon, and then the name of the stream. For more information, see <a href=""https://docs.microsoft.com/windows/desktop/FileIO/file-streams"">File Streams</a>. <div class=""alert""><b>Tip</b>  Starting with Windows 10, version 1607, for the unicode version of this function (<b>CreateFileW</b>), you can opt-in to remove the <b>MAX_PATH</b> limitation without prepending ""\\?\"". See the ""Maximum Path Length Limitation"" section of <a href=""https://docs.microsoft.com/windows/desktop/FileIO/naming-a-file"">Naming Files, Paths, and Namespaces</a> for details.</div> <div> </div></para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""dwDesiredAccess"">
		/// <para>The requested access to the file or device, which can be summarized as read, write, both or neither zero). The most commonly used values are <b>GENERIC_READ</b>, <b>GENERIC_WRITE</b>, or both (<c>GENERIC_READ | GENERIC_WRITE</c>). For more information, see <a href=""https://docs.microsoft.com/windows/desktop/SecAuthZ/generic-access-rights"">Generic Access Rights</a>, <a href=""https://docs.microsoft.com/windows/desktop/FileIO/file-security-and-access-rights"">File Security and Access Rights</a>, <a href=""https://docs.microsoft.com/windows/desktop/FileIO/file-access-rights-constants"">File Access Rights Constants</a>, and <a href=""https://docs.microsoft.com/windows/desktop/SecAuthZ/access-mask"">ACCESS_MASK</a>. If this parameter is zero, the application can query certain metadata such as file, directory, or device attributes without accessing that file or device, even if <b>GENERIC_READ</b> access would have been denied. You cannot request an access mode that conflicts with the sharing mode that is specified by the <i>dwShareMode</i> parameter in an open request that already has an open handle. For more information, see the Remarks section of this topic and <a href=""https://docs.microsoft.com/windows/desktop/FileIO/creating-and-opening-files"">Creating and Opening Files</a>.</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""dwShareMode"">
		/// <para>The requested sharing mode of the file or device, which can be read, write, both, delete, all of these, or none (refer to the following table). Access requests to attributes or extended attributes are not affected by this flag. If this parameter is zero and <b>CreateFile</b> succeeds, the file or device cannot be shared and cannot be opened again until the handle to the file or device is closed. For more information, see the Remarks section. You cannot request a sharing mode that conflicts with the access mode that is specified in an existing request that has an open handle. <b>CreateFile</b> would fail and the <a href=""https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror"">GetLastError</a> function would return <b>ERROR_SHARING_VIOLATION</b>. To enable a process to share a file or device while another process has the file or device open, use a</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""lpSecurityAttributes"">
		/// <para>A pointer to a <a href=""https://docs.microsoft.com/previous-versions/windows/desktop/legacy/aa379560(v=vs.85)"">SECURITY_ATTRIBUTES</a> structure that contains two separate but related data members: an optional security descriptor, and a Boolean value that determines whether the returned handle can be inherited by child processes. This parameter can be <b>NULL</b>. If this parameter is <b>NULL</b>, the handle returned by <b>CreateFile</b> cannot be inherited by any child processes the application may create and the file or device associated with the returned handle gets a default security descriptor. The <b>lpSecurityDescriptor</b> member of the structure specifies a <a href=""https://docs.microsoft.com/windows/desktop/api/winnt/ns-winnt-security_descriptor"">SECURITY_DESCRIPTOR</a> for a file or device. If this member is <b>NULL</b>, the file or device associated with the returned handle is assigned a default security descriptor. <b>CreateFile</b> ignores the <b>lpSecurityDescriptor</b> member when opening an existing file or device, but continues to use the <b>bInheritHandle</b> member. The <b>bInheritHandle</b>member of the structure specifies whether the returned handle can be inherited. For more information, see the Remarks section.</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""dwCreationDisposition"">
		/// <para>An action to take on a file or device that exists or does not exist. For devices other than files, this parameter is usually set to <b>OPEN_EXISTING</b>. For more information, see the Remarks section.</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""dwFlagsAndAttributes"">
		/// <para>The file or device attributes and flags, <b>FILE_ATTRIBUTE_NORMAL</b> being the most common default value for files. This parameter can include any combination of the available file attributes (<b>FILE_ATTRIBUTE_*</b>). All other file attributes override <b>FILE_ATTRIBUTE_NORMAL</b>. This parameter can also contain combinations of flags (<b>FILE_FLAG_*</b>) for control of file or device caching behavior, access modes, and other special-purpose flags. These combine with any <b>FILE_ATTRIBUTE_*</b> values. This parameter can also contain Security Quality of Service (SQOS) information by specifying the <b>SECURITY_SQOS_PRESENT</b> flag. Additional SQOS-related flags information is presented in the table following the attributes and flags tables. <div class=""alert""><b>Note</b>  When <b>CreateFile</b> opens an existing file, it generally combines the file flags with the file attributes of the existing file, and ignores any file attributes supplied as part of <i>dwFlagsAndAttributes</i>. Special cases are detailed in <a href=""https://docs.microsoft.com/windows/desktop/FileIO/creating-and-opening-files"">Creating and Opening Files</a>.</div> <div> </div> Some of the following file attributes and flags may only apply to files and not necessarily all other types of devices that <b>CreateFile</b> can open. For additional information, see the Remarks section of this topic and <a href=""https://docs.microsoft.com/windows/desktop/FileIO/creating-and-opening-files"">Creating and Opening Files</a>. For more advanced access to file attributes, see <a href=""https://docs.microsoft.com/windows/desktop/api/fileapi/nf-fileapi-setfileattributesa"">SetFileAttributes</a>. For a complete list of all file attributes with their values and descriptions, see <a href=""https://docs.microsoft.com/windows/desktop/FileIO/file-attribute-constants"">File Attribute Constants</a>. </para>
		/// <para>This doc was truncated.</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <param name=""hTemplateFile"">
		/// <para>A valid handle to a template file with the <b>GENERIC_READ</b> access right. The template file supplies file attributes and extended attributes for the file that is being created. This parameter can be <b>NULL</b>. When opening an existing file, <b>CreateFile</b> ignores this parameter. When opening a new encrypted file, the file inherits the discretionary access control list from its parent directory. For additional information, see <a href=""https://docs.microsoft.com/windows/desktop/FileIO/file-encryption"">File Encryption</a>.</para>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew#parameters"">Read more on docs.microsoft.com</see>.</para>
		/// </param>
		/// <returns>
		/// <para>If the function succeeds, the return value is an open handle to the specified file, device, named pipe, or mail slot. If the function fails, the return value is <b>INVALID_HANDLE_VALUE</b>. To get extended error information, call <a href=""/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror"">GetLastError</a>.</para>
		/// </returns>
		/// <remarks>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//fileapi/nf-fileapi-createfilew"">Learn more about this API from docs.microsoft.com</see>.</para>
		/// </remarks>
		[DllImport(""Kernel32"", ExactSpelling = true, EntryPoint = ""CreateFileW"", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static extern unsafe win32.Foundation.HANDLE CreateFile(win32.Foundation.PCWSTR lpFileName, win32.Storage.FileSystem.FILE_ACCESS_FLAGS dwDesiredAccess, win32.Storage.FileSystem.FILE_SHARE_MODE dwShareMode, [Optional] win32.Security.SECURITY_ATTRIBUTES* lpSecurityAttributes, win32.Storage.FileSystem.FILE_CREATION_DISPOSITION dwCreationDisposition, win32.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES dwFlagsAndAttributes, win32.Foundation.HANDLE hTemplateFile);
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.PWSTR.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Foundation
	{
		[DebuggerDisplay(""{Value}"")]
		internal unsafe readonly partial struct PWSTR
			: IEquatable<PWSTR>
		{
			internal readonly char* Value;
			internal PWSTR(char* value) => this.Value = value;
			public static implicit operator char*(PWSTR value) => value.Value;
			public static implicit operator PWSTR(char* value) => new PWSTR(value);
			public static bool operator ==(PWSTR left, PWSTR right) => left.Value == right.Value;
			public static bool operator !=(PWSTR left, PWSTR right) => !(left == right);

			public bool Equals(PWSTR other) => this.Value == other.Value;

			public override bool Equals(object obj) => obj is PWSTR other && this.Equals(other);

			public override int GetHashCode() => checked((int)this.Value);

			internal int Length
			{
				get
				{
					char* p = this.Value;
					if (p is null)
						return 0;
					while (*p != '\0')
						p++;
					return checked((int)(p - this.Value));
				}
			}

			public override string ToString() => this.Value is null ? null : new string(this.Value);

			internal Span<char> AsSpan() => this.Value is null ? default(Span<char>) : new Span<char>(this.Value, this.Length);
		}
	}
}
".Replace("\r\n", "\n")),
                    (typeof(SourceGenerator), "Windows.Win32.SECURITY_ATTRIBUTES.g.cs", @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658,CS0436
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using win32 = global::Windows.Win32;

	namespace Security
	{
		/// <summary>The SECURITY_ATTRIBUTES structure contains the security descriptor for an object and specifies whether the handle retrieved by specifying this structure is inheritable.</summary>
		/// <remarks>
		/// <para><see href=""https://docs.microsoft.com/windows/win32/api//wtypesbase/ns-wtypesbase-security_attributes#"">Read more on docs.microsoft.com</see>.</para>
		/// </remarks>
		internal partial struct SECURITY_ATTRIBUTES
		{
			/// <summary>The size, in bytes, of this structure. Set this value to the size of the **SECURITY\_ATTRIBUTES** structure.</summary>
			internal uint nLength;
			/// <summary>
			/// <para>A pointer to a [**SECURITY\_DESCRIPTOR**](../winnt/ns-winnt-security_descriptor.md) structure that controls access to the object. If the value of this member is **NULL**, the object is assigned the default security descriptor associated with the [*access token*](/windows/win32/secauthz/access-tokens) of the calling process. This is not the same as granting access to everyone by assigning a **NULL** [*discretionary access control list*](/windows/win32/secauthz/dacls-and-aces) (DACL). By default, the default DACL in the access token of a process allows access only to the user represented by the access token. For information about creating a security descriptor, see [Creating a Security Descriptor](/windows/win32/secauthz/creating-a-security-descriptor-for-a-new-object-in-c--).</para>
			/// <para><see href=""https://docs.microsoft.com/windows/win32/api//wtypesbase/ns-wtypesbase-security_attributes#members"">Read more on docs.microsoft.com</see>.</para>
			/// </summary>
			internal unsafe void* lpSecurityDescriptor;
			/// <summary>A Boolean value that specifies whether the returned handle is inherited when a new process is created. If this member is **TRUE**, the new process inherits the handle.</summary>
			internal win32.Foundation.BOOL bInheritHandle;
		}
	}
}
".Replace("\r\n", "\n")),
                },
            },
        }.RunAsync();
    }

    [Fact]
    public async Task UnparseableNativeMethodsJson()
    {
        await new VerifyTest
        {
            TestState =
            {
                ReferenceAssemblies = MyReferenceAssemblies.NetStandard20,
                AdditionalFiles =
                {
                    ("NativeMethods.txt", "CreateFile"),
                    ("NativeMethods.json", @"{ ""allowMarshaling"": f }"), // the point where the user is typing "false"
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", ConstructGlobalConfigString()),
                },
                GeneratedSources =
                {
                    // Nothing generated, but no exceptions thrown that would lead Roslyn to disable the source generator in the IDE either.
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(SourceGenerator.OptionsParsingError.Id, DiagnosticSeverity.Error),
                },
            },
        }.RunAsync();
    }

    private static string ConstructGlobalConfigString(bool omitDocs = false)
    {
        StringBuilder globalConfigBuilder = new();
        globalConfigBuilder.AppendLine("is_global = true");
        globalConfigBuilder.AppendLine();
        globalConfigBuilder.AppendLine($"build_property.CsWin32InputMetadataPaths = {JoinAssemblyMetadata("ProjectionMetadataWinmd")}");
        if (!omitDocs)
        {
            globalConfigBuilder.AppendLine($"build_property.CsWin32InputDocPaths = {JoinAssemblyMetadata("ProjectionDocs")}");
        }

        return globalConfigBuilder.ToString();

        static string JoinAssemblyMetadata(string name)
        {
            return string.Join(";", typeof(GeneratorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(metadata => metadata.Key == name).Select(metadata => metadata.Value));
        }
    }

    private static ImmutableArray<Diagnostic> FilterDiagnostics(ImmutableArray<Diagnostic> diagnostics) => diagnostics.Where(d => d.Severity > DiagnosticSeverity.Hidden).ToImmutableArray();

    private static bool IsAttributePresent(AttributeListSyntax al, string attributeName) => al.Attributes.Any(a => a.Name.ToString() == attributeName);

    private static IEnumerable<AttributeSyntax> FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name) => attributeLists.SelectMany(al => al.Attributes).Where(a => a.Name.ToString() == name);

    private static void AssertConsistentLineEndings(Compilation compilation)
    {
        foreach (SyntaxTree doc in compilation.SyntaxTrees)
        {
            AssertConsistentLineEndings(doc);
        }
    }

    private static void AssertConsistentLineEndings(SyntaxTree syntaxTree)
    {
        SourceText sourceText = syntaxTree.GetText();
        int firstLineBreakLength = default;
        int lineCount = 1;
        foreach (TextLine line in sourceText.Lines)
        {
            int thisLineBreakLength = line.EndIncludingLineBreak - line.End;
            if (lineCount == 1)
            {
                firstLineBreakLength = thisLineBreakLength;
            }
            else
            {
                if (firstLineBreakLength != thisLineBreakLength && thisLineBreakLength > 0)
                {
                    Assert.False(true, $"{syntaxTree.FilePath} Line {lineCount} had a {thisLineBreakLength}-byte line ending but line 1's line ending was {firstLineBreakLength} bytes long.");
                }
            }

            lineCount++;
        }
    }

    private CSharpCompilation AddGeneratedCode(CSharpCompilation compilation, Generator generator)
    {
        var compilationUnits = generator.GetCompilationUnits(CancellationToken.None);
        var syntaxTrees = new List<SyntaxTree>(compilationUnits.Count);
        foreach (var unit in compilationUnits)
        {
            // Our syntax trees aren't quite right. And anyway the source generator API only takes text anyway so it doesn't _really_ matter.
            // So render the trees as text and have C# re-parse them so we get the same compiler warnings/errors that the user would get.
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(unit.Value.ToFullString(), this.parseOptions, path: unit.Key));
        }

        return compilation.AddSyntaxTrees(syntaxTrees);
    }

    private void CollectGeneratedCode(Generator generator) => this.compilation = this.AddGeneratedCode(this.compilation, generator);

    private IEnumerable<MethodDeclarationSyntax> FindGeneratedMethod(string name) => this.compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).Where(md => md.Identifier.ValueText == name);

    private IEnumerable<BaseTypeDeclarationSyntax> FindGeneratedType(string name) => this.compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()).Where(btd => btd.Identifier.ValueText == name);

    private IEnumerable<FieldDeclarationSyntax> FindGeneratedConstant(string name) => this.compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>()).Where(fd => (fd.Modifiers.Any(SyntaxKind.StaticKeyword) || fd.Modifiers.Any(SyntaxKind.ConstKeyword)) && fd.Declaration.Variables.Any(vd => vd.Identifier.ValueText == name));

    private bool IsMethodGenerated(string name) => this.FindGeneratedMethod(name).Any();

    private void AssertNoDiagnostics(bool logGeneratedCode = true) => this.AssertNoDiagnostics(this.compilation, logGeneratedCode);

    private void AssertNoDiagnostics(CSharpCompilation compilation, bool logAllGeneratedCode = true)
    {
        var diagnostics = FilterDiagnostics(compilation.GetDiagnostics());
        this.LogDiagnostics(diagnostics);

        var emitDiagnostics = ImmutableArray<Diagnostic>.Empty;
        bool? emitSuccessful = null;
        if (diagnostics.IsEmpty)
        {
            var emitResult = compilation.Emit(peStream: Stream.Null, xmlDocumentationStream: Stream.Null);
            emitSuccessful = emitResult.Success;
            emitDiagnostics = FilterDiagnostics(emitResult.Diagnostics);
            this.LogDiagnostics(emitDiagnostics);
        }

        if (logAllGeneratedCode)
        {
            this.LogGeneratedCode(compilation);
        }
        else
        {
            foreach (SyntaxTree? fileWithDiagnosticts in diagnostics.Select(d => d.Location.SourceTree).Distinct())
            {
                if (fileWithDiagnosticts is object)
                {
                    this.LogGeneratedCode(fileWithDiagnosticts);
                }
            }
        }

        Assert.Empty(diagnostics);
        if (emitSuccessful.HasValue)
        {
            Assert.Empty(emitDiagnostics);
            Assert.True(emitSuccessful);
        }

        AssertConsistentLineEndings(compilation);
    }

    private void LogDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }
    }

    private void LogGeneratedCode(CSharpCompilation compilation)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            this.LogGeneratedCode(tree);
        }
    }

    private void LogGeneratedCode(SyntaxTree tree)
    {
        this.logger.WriteLine(FileSeparator);
        this.logger.WriteLine($"{tree.FilePath} content:");
        this.logger.WriteLine(FileSeparator);
        using var lineWriter = new NumberedLineWriter(this.logger);
        tree.GetRoot().WriteTo(lineWriter);
        lineWriter.WriteLine(string.Empty);
    }

    private void AssertGeneratedType(string apiName, string expectedSyntax, string? expectedExtensions = null)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        BaseTypeDeclarationSyntax syntax = Assert.Single(this.FindGeneratedType(apiName));
        Assert.Equal(TestUtils.NormalizeToExpectedLineEndings(expectedSyntax), TestUtils.NormalizeToExpectedLineEndings(syntax.ToFullString()));

        var extensionsClass = (ClassDeclarationSyntax?)this.FindGeneratedType("InlineArrayIndexerExtensions").SingleOrDefault();
        if (expectedExtensions is string)
        {
            Assert.NotNull(extensionsClass);
            string extensionsClassString = extensionsClass!.ToFullString();
            Assert.Equal(TestUtils.NormalizeToExpectedLineEndings(expectedExtensions), TestUtils.NormalizeToExpectedLineEndings(extensionsClassString));
        }
        else
        {
            // Assert that no indexer was generated.
            Assert.Null(extensionsClass);
        }
    }

    private void AssertGeneratedMember(string apiName, string memberName, string expectedSyntax)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        BaseTypeDeclarationSyntax typeSyntax = Assert.Single(this.FindGeneratedType(apiName));
        var semanticModel = this.compilation.GetSemanticModel(typeSyntax.SyntaxTree, ignoreAccessibility: false);
        var member = Assert.Single(semanticModel.GetDeclaredSymbol(typeSyntax, CancellationToken.None)!.GetMembers(memberName));
        var memberSyntax = member.DeclaringSyntaxReferences.Single().GetSyntax(CancellationToken.None);
        Assert.Equal(
            TestUtils.NormalizeToExpectedLineEndings(expectedSyntax).Trim(),
            TestUtils.NormalizeToExpectedLineEndings(memberSyntax.ToFullString()).Trim());
    }

    private async Task<CSharpCompilation> CreateCompilationAsync(ReferenceAssemblies references, Platform platform = Platform.AnyCpu)
    {
        ImmutableArray<MetadataReference> metadataReferences = await references.ResolveAsync(LanguageNames.CSharp, default);

        // Workaround for https://github.com/dotnet/roslyn-sdk/issues/699
        metadataReferences = metadataReferences.AddRange(
            Directory.GetFiles(Path.Combine(Path.GetTempPath(), "test-packages", "Microsoft.Windows.SDK.Contracts.10.0.19041.1", "ref", "netstandard2.0"), "*.winmd").Select(p => MetadataReference.CreateFromFile(p)));

        // CONSIDER: How can I pass in the source generator itself, with AdditionalFiles, so I'm exercising that code too?
        var compilation = CSharpCompilation.Create(
            assemblyName: "test",
            references: metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform, allowUnsafe: true));

        // Add namespaces that projects may define to ensure we prefix types with "global::" everywhere.
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.System { }", this.parseOptions, path: "Microsoft.System.cs"),
            CSharpSyntaxTree.ParseText("namespace Windows.Win32.System { }", this.parseOptions, path: "Windows.Win32.System.cs"));

        return compilation;
    }

    private Generator CreateGenerator(GeneratorOptions? options = null, CSharpCompilation? compilation = null) => this.CreateGenerator(MetadataPath, options, compilation);

    private Generator CreateGenerator(string path, GeneratorOptions? options = null, CSharpCompilation? compilation = null) => new Generator(path, Docs.Get(ApiDocsPath), options ?? DefaultTestGeneratorOptions, compilation ?? this.compilation, this.parseOptions);

    private static class MyReferenceAssemblies
    {
#pragma warning disable SA1202 // Elements should be ordered by access
        private static readonly ImmutableArray<PackageIdentity> AdditionalPackages = ImmutableArray.Create(new PackageIdentity("Microsoft.Windows.SDK.Contracts", "10.0.19041.1"));

        internal static readonly ReferenceAssemblies NetStandard20 = ReferenceAssemblies.NetStandard.NetStandard20.AddPackages(AdditionalPackages.Add(new PackageIdentity("System.Memory", "4.5.4")));

        internal static class NetFramework
        {
            internal static readonly ReferenceAssemblies Net35 = ReferenceAssemblies.NetFramework.Net35.Default.AddPackages(AdditionalPackages);

            internal static readonly ReferenceAssemblies Net40 = ReferenceAssemblies.NetFramework.Net40.Default.AddPackages(AdditionalPackages);
        }

        internal static class Net
        {
            internal static readonly ReferenceAssemblies Net50 = ReferenceAssemblies.Net.Net50.AddPackages(AdditionalPackages);
        }
#pragma warning restore SA1202 // Elements should be ordered by access
    }
}
