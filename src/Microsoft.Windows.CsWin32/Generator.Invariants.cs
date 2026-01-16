// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal const string InteropDecorationNamespace = "Windows.Win32.Foundation.Metadata";
    internal const string NativeArrayInfoAttribute = "NativeArrayInfoAttribute";
    internal const string NativeBitfieldAttribute = "NativeBitfieldAttribute";
    internal const string MemorySizeAttribute = "MemorySizeAttribute";
    internal const string RAIIFreeAttribute = "RAIIFreeAttribute";
    internal const string DoNotReleaseAttribute = "DoNotReleaseAttribute";
    internal const string AssociatedEnumAttribute = "AssociatedEnumAttribute";
    internal const string AssociatedConstantAttribute = "AssociatedConstantAttribute";
    internal const string GlobalNamespacePrefix = "global::";
    internal const string GlobalWinmdRootNamespaceAlias = "winmdroot";
    internal const string WinRTCustomMarshalerClass = "WinRTCustomMarshaler";
    internal const string WinRTCustomMarshalerNamespace = "Windows.Win32.CsWin32.InteropServices";
    internal const string WinRTCustomMarshalerFullName = WinRTCustomMarshalerNamespace + "." + WinRTCustomMarshalerClass;
    internal const string UnmanagedInteropSuffix = "_unmanaged";

    internal static readonly SyntaxAnnotation IsRetValAnnotation = new SyntaxAnnotation("RetVal");
    internal static readonly IdentifierNameSyntax NestedCOMInterfaceName = IdentifierName("Interface");

    /// <summary>
    /// A map of .NET interop structs to use, keyed by the native structs that should <em>not</em> be generated.
    /// </summary>
    /// <devremarks>
    /// When adding to this dictionary, consider also adding to <see cref="BannedAPIsWithoutMarshaling"/>.
    /// </devremarks>
    internal static readonly Dictionary<string, TypeSyntax> BclInteropStructs = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal)
    {
        { nameof(System.Runtime.InteropServices.ComTypes.FILETIME), ParseTypeName("global::System.Runtime.InteropServices.ComTypes.FILETIME") },
        { nameof(Guid), ParseTypeName("global::System.Guid") },
        { "OLD_LARGE_INTEGER", PredefinedType(Token(SyntaxKind.LongKeyword)) },
        { "LARGE_INTEGER", PredefinedType(Token(SyntaxKind.LongKeyword)) },
        { "ULARGE_INTEGER", PredefinedType(Token(SyntaxKind.ULongKeyword)) },
        { "OVERLAPPED", ParseTypeName("global::System.Threading.NativeOverlapped") },
        { "POINT", ParseTypeName("global::System.Drawing.Point") },
        { "POINTF", ParseTypeName("global::System.Drawing.PointF") },
        { "STREAM_SEEK", ParseTypeName("global::System.IO.SeekOrigin") },
    };

    /// <summary>
    /// A map of .NET interop structs to use, keyed by the native structs that should <em>not</em> be generated <em>when marshaling is enabled.</em>
    /// That is, these interop types should only be generated when marshaling is disabled.
    /// </summary>
    /// <devremarks>
    /// When adding to this dictionary, consider also adding to <see cref="BannedAPIsWithMarshaling"/>.
    /// </devremarks>
    internal static readonly Dictionary<string, TypeSyntax> AdditionalBclInteropStructsMarshaled = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal)
    {
        { nameof(System.Runtime.InteropServices.ComTypes.IDataObject), ParseTypeName("global::System.Runtime.InteropServices.ComTypes.IDataObject") },
    };

    internal static readonly Dictionary<string, TypeSyntax> BclInteropSafeHandles = new Dictionary<string, TypeSyntax>(StringComparer.Ordinal)
    {
        { "CloseHandle", ParseTypeName("Microsoft.Win32.SafeHandles.SafeFileHandle") },
        { "RegCloseKey", ParseTypeName("Microsoft.Win32.SafeHandles.SafeRegistryHandle") },
    };

    internal static readonly HashSet<string> SpecialTypeDefNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "PCSTR",
        "PCWSTR",
        "PCZZSTR",
        "PCZZWSTR",
        "PZZSTR",
        "PZZWSTR",
    };

    private const string SystemRuntimeCompilerServices = "System.Runtime.CompilerServices";
    private const string SystemRuntimeInteropServices = "System.Runtime.InteropServices";
    private const string SystemRuntimeInteropServicesMarshalling = "System.Runtime.InteropServices.Marshalling";
    private const string NativeTypedefAttribute = "NativeTypedefAttribute";
    private const string MetadataTypedefAttribute = "MetadataTypedefAttribute";
    private const string AlsoUsableForAttribute = "AlsoUsableForAttribute";
    private const string InvalidHandleValueAttribute = "InvalidHandleValueAttribute";
    private const string CanReturnMultipleSuccessValuesAttribute = "CanReturnMultipleSuccessValuesAttribute";
    private const string FlexibleArrayAttribute = "FlexibleArrayAttribute";
    private const string CanReturnErrorsAsSuccessAttribute = "CanReturnErrorsAsSuccessAttribute";
    private const string SimpleFileNameAnnotation = "SimpleFileName";
    private const string NamespaceContainerAnnotation = "NamespaceContainer";
    private const string OriginalDelegateAnnotation = "OriginalDelegate";

    private static readonly Dictionary<string, MethodDeclarationSyntax> PInvokeHelperMethods;
    private static readonly InterfaceDeclarationSyntax IVTableInterface;
    private static readonly InterfaceDeclarationSyntax IVTableGenericInterface;
    private static readonly Dictionary<string, MethodDeclarationSyntax> Win32SdkMacros;

    private static readonly string AutoGeneratedHeader = @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

".Replace("\r\n", "\n");

    private static readonly string PartialPInvokeContentComment = @"
/// <content>
/// Contains extern methods from ""{0}"".
/// </content>
".Replace("\r\n", "\n");

    private static readonly string PartialPInvokeMacrosContentComment = @"
/// <content>
/// Contains macros.
/// </content>
".Replace("\r\n", "\n");

    private static readonly SyntaxTriviaList InlineArrayUnsafeAsSpanComment = ParseLeadingTrivia(@"/// <summary>
/// Gets this inline array as a span.
/// </summary>
/// <remarks>
/// ⚠ Important ⚠: When this struct is on the stack, do not let the returned span outlive the stack frame that defines it.
/// </remarks>
");

    private static readonly SyntaxTriviaList InlineArrayUnsafeIndexerComment = ParseLeadingTrivia(@"/// <summary>
/// Gets a ref to an individual element of the inline array.
/// ⚠ Important ⚠: When this struct is on the stack, do not let the returned reference outlive the stack frame that defines it.
/// </summary>
");

    private static readonly SyntaxTriviaList InlineCharArrayToStringComment = ParseLeadingTrivia(@"/// <summary>
/// Copies the fixed array to a new string, stopping before the first null terminator character or at the end of the fixed array (whichever is shorter).
/// </summary>
");

    private static readonly SyntaxTriviaList InlineCharArrayToStringWithLengthComment = ParseLeadingTrivia(@"/// <summary>
/// Copies the fixed array to a new string up to the specified length regardless of whether there are null terminating characters.
/// </summary>
/// <exception cref=""ArgumentOutOfRangeException"">
/// Thrown when <paramref name=""length""/> is less than <c>0</c> or greater than <see cref=""Length""/>.
/// </exception>
");

    private static readonly SyntaxTriviaList StrAsSpanComment = ParseLeadingTrivia(@"/// <summary>
/// Returns a span of the characters in this string.
/// </summary>
");

    /// <summary>
    /// The initial set of libraries that are expected to be allowed next to an application instead of being required to load from System32.
    /// </summary>
    /// <remarks>
    /// This list is combined with an MSBuild item list so that 3rd party metadata can document app-local DLLs.
    /// </remarks>
    /// <see href="https://learn.microsoft.com/windows/win32/debug/dbghelp-versions" />
    private static readonly string[] BuiltInAppLocalLibraries = ["DbgHelp.dll", "SymSrv.dll", "SrcSrv.dll"];

    // [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static readonly AttributeSyntax DefaultDllImportSearchPathsAttribute =
        Attribute(IdentifierName("DefaultDllImportSearchPaths")).AddArgumentListArguments(
            AttributeArgument(CompoundExpression(
                SyntaxKind.BitwiseOrExpression,
                IdentifierName(nameof(DllImportSearchPath)),
                nameof(DllImportSearchPath.System32))));

    // [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | ...)]
    private static readonly AttributeSyntax DefaultDllImportSearchPathsAllowAppDirAttribute =
        Attribute(IdentifierName("DefaultDllImportSearchPaths")).AddArgumentListArguments(
            AttributeArgument(CompoundExpression(
                SyntaxKind.BitwiseOrExpression,
                IdentifierName(nameof(DllImportSearchPath)),
                nameof(DllImportSearchPath.System32),
                nameof(DllImportSearchPath.ApplicationDirectory),
                nameof(DllImportSearchPath.AssemblyDirectory))));

    private static readonly AttributeSyntax GeneratedCodeAttribute = Attribute(IdentifierName("global::System.CodeDom.Compiler.GeneratedCode"))
        .WithArgumentList(FixTrivia(AttributeArgumentList(
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(ThisAssembly.AssemblyName))),
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(ThisAssembly.AssemblyInformationalVersion))))));

    private static readonly HashSet<string> ImplicitConversionTypeDefs = new HashSet<string>(StringComparer.Ordinal)
    {
        "PWSTR",
        "PSTR",
        "LPARAM",
        "WPARAM",
    };

    private static readonly HashSet<string> TypeDefsThatDoNotNestTheirConstants = new HashSet<string>(SpecialTypeDefNames, StringComparer.Ordinal)
    {
        "PWSTR",
        "PSTR",
    };

    /// <summary>
    /// This is the preferred capitalizations for modules and class names.
    /// If they are not in this list, the capitalization will come from the metadata assembly.
    /// </summary>
    private static readonly ImmutableHashSet<string> CanonicalCapitalizations = ImmutableHashSet.Create<string>(
        StringComparer.OrdinalIgnoreCase,
        "AdvApi32",
        "AuthZ",
        "BCrypt",
        "Cabinet",
        "CfgMgr32",
        "Chakra",
        "CodeGeneration",
        "CodeGeneration.Debugging",
        "CodeGenerationAttributes",
        "ComCtl32",
        "ComDlg32",
        "Crypt32",
        "CryptNet",
        "D3D11",
        "D3D12",
        "D3DCompiler_47",
        "DbgHelp",
        "DfsCli",
        "DhcpCSvc",
        "DhcpCSvc6",
        "DnsApi",
        "DsParse",
        "DSRole",
        "DwmApi",
        "DXGI",
        "Esent",
        "FltLib",
        "Fusion",
        "Gdi32",
        "Hid",
        "Icu",
        "ImageHlp",
        "InkObjCore",
        "IPHlpApi",
        "Kernel32",
        "LogonCli",
        "Magnification",
        "MFSensorGroup",
        "Mpr",
        "MSCms",
        "MSCorEE",
        "Msi",
        "MswSock",
        "NCrypt",
        "NetApi32",
        "NetUtils",
        "NewDev",
        "NTDll",
        "Ole32",
        "OleAut32",
        "PowrProf",
        "PropSys",
        "Psapi",
        "RpcRT4",
        "SamCli",
        "SchedCli",
        "SetupApi",
        "SHCore",
        "Shell32",
        "ShlwApi",
        "SrvCli",
        "TokenBinding",
        "UrlMon",
        "User32",
        "UserEnv",
        "UxTheme",
        "Version",
        "WebAuthN",
        "WebServices",
        "WebSocket",
        "Win32",
        "Win32MetaGeneration",
        "Windows.Core",
        "Windows.ShellScalingApi",
        "WinHttp",
        "WinMM",
        "WinUsb",
        "WksCli",
        "WLanApi",
        "WldAp32",
        "WtsApi32");

    private static readonly HashSet<string> ObjectMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetType",
    };

    private static readonly string[] WarningsToSuppressInGeneratedCode = new string[]
    {
        "CS1591", // missing docs
        "CS1573", // missing docs for an individual parameter
        "CS0465", // Avoid methods named "Finalize", which can't be helped
        "CS0649", // fields never assigned to
        "CS8019", // unused usings
        "CS1570", // XML comment has badly formed XML
        "CS1584", // C# bug: https://github.com/microsoft/CsWin32/issues/24
        "CS1658", // C# bug: https://github.com/microsoft/CsWin32/issues/24
        "CS0436", // conflicts with the imported type (InternalsVisibleTo between two projects that both use CsWin32)
        "CS8981", // The type name only contains lower-cased ascii characters
        "SYSLIB1092", // The return value in the managed definition will be converted to an 'out' parameter when calling the unmanaged COM method
    };

    private static readonly SyntaxTriviaList FileHeader = ParseLeadingTrivia(AutoGeneratedHeader).Add(
        Trivia(PragmaWarningDirectiveTrivia(
            disableOrRestoreKeyword: TokenWithSpace(SyntaxKind.DisableKeyword),
            errorCodes: [.. WarningsToSuppressInGeneratedCode.Select(IdentifierName)],
            isActive: true)));

    private static readonly AttributeSyntax InAttributeSyntax = Attribute(IdentifierName("In")).WithArgumentList(null);
    private static readonly AttributeSyntax OutAttributeSyntax = Attribute(IdentifierName("Out")).WithArgumentList(null);
    private static readonly AttributeSyntax OptionalAttributeSyntax = Attribute(IdentifierName("Optional")).WithArgumentList(null);
    private static readonly AttributeSyntax FlagsAttributeSyntax = Attribute(IdentifierName("Flags")).WithArgumentList(null);
    private static readonly AttributeListSyntax CsWin32StampAttribute = AttributeList(
        Attribute(ParseName("global::System.Reflection.AssemblyMetadata")).AddArgumentListArguments(
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(ThisAssembly.AssemblyName))),
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(ThisAssembly.AssemblyInformationalVersion)))))
        .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)));

    /// <summary>
    /// Gets the set of macros that can be generated.
    /// </summary>
    public static IEnumerable<string> AvailableMacros => Win32SdkMacros.Keys;

    /// <summary>
    /// Gets a map of interop APIs that should never be generated, whether marshaling is allowed or not, and messages to emit in diagnostics if these APIs are ever directly requested.
    /// </summary>
    internal static ImmutableDictionary<string, string> BannedAPIsWithoutMarshaling { get; } = ImmutableDictionary<string, string>.Empty
        .Add("GetLastError", "Do not generate GetLastError. Call Marshal.GetLastWin32Error() instead. Learn more from https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.getlastwin32error")
        .Add("OLD_LARGE_INTEGER", "Use the C# long keyword instead.")
        .Add("LARGE_INTEGER", "Use the C# long keyword instead.")
        .Add("ULARGE_INTEGER", "Use the C# ulong keyword instead.")
        .Add("OVERLAPPED", "Use System.Threading.NativeOverlapped instead.")
        .Add("POINT", "Use System.Drawing.Point instead.")
        .Add("POINTF", "Use System.Drawing.PointF instead.")
        .Add("AddVectoredExceptionHandler", "SEH native function pointers are unstable when implemented by .NET managed code. Learn more at https://github.com/microsoft/CsWin32/issues/1253#issuecomment-2341562332")
        .Add("RemoveVectoredExceptionHandler", "SEH native function pointers are unstable when implemented by .NET managed code. Learn more at https://github.com/microsoft/CsWin32/issues/1253#issuecomment-2341562332");

    /// <summary>
    /// Gets a map of interop APIs that should not be generated when marshaling is allowed, and messages to emit in diagnostics if these APIs are ever directly requested.
    /// </summary>
    internal static ImmutableDictionary<string, string> BannedAPIsWithMarshaling { get; } = BannedAPIsWithoutMarshaling
        .Add("IUnknown", "This COM interface is implicit in the runtime. Interfaces that derive from it should apply the [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] attribute instead.")
        .Add("IDispatch", "This COM interface is implicit in the runtime. Interfaces that derive from it should apply the [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)] attribute instead.")
        .Add("VARIANT", "Use `object` instead of VARIANT when in COM interface mode. VARIANT can only be emitted when emitting COM interfaces as structs.");

    private string Win32NamespacePrefixString => this.IsWin32Sdk ? GlobalWinmdRootNamespaceAlias : "global::Windows.Win32";

    private IdentifierNameSyntax Win32NamespacePrefix => IdentifierName(this.Win32NamespacePrefixString);

    private TypeSyntax HresultTypeSyntax
    {
        get
        {
            return QualifiedName(QualifiedName(this.Win32NamespacePrefix, IdentifierName("Foundation")), IdentifierName("HRESULT"));
        }
    }
}
