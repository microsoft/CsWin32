// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Opt-in three-way compatibility checks against a separately built, pinned baseline generator.
/// </summary>
public class HeaderAnnotationCompatibilityTests : GeneratorTestBase
{
    private const string OrdinaryFieldCaller = """
        namespace Windows.Win32.Test
        {
            static class FieldCaller
            {
                static MODE Use(RECORD record)
                {
                    record.Mode = MODE.First | MODE.Second;
                    return record.Mode;
                }
            }
        }
        """;

    private const string PropertyInterfaceCaller = """
        namespace Windows.Win32.Test
        {
            /// <summary>Represents a caller-defined property contract.</summary>
            public interface IModeProperty
            {
                /// <summary>Gets or sets the mode.</summary>
                MODE Mode { get; set; }
            }
            /// <summary>Extends the generated struct with a caller contract.</summary>
            public partial struct RECORD : IModeProperty { }
        }
        """;

    private static readonly string[] CanonicalRequests = ["RECORD", "RoundTrip", "PointerReturn", "Buffer", "Output", "DoublePointer", "UseCallback", "Unannotated"];
    private static readonly string[] WinHttpRequests =
    [
        "WinHttpOpen", "WinHttpConnect", "WinHttpOpenRequest", "WinHttpCloseHandle", "WinHttpCreateUrl", "WinHttpCrackUrl",
        "WinHttpSetStatusCallback", "WinHttpQueryHeadersEx", "WinHttpReadData", "WinHttpGetProxyForUrl", "WinHttpCreateProxyResolver",
        "WINHTTP_CREDS", "WINHTTP_CREDS_EX", "WINHTTP_PROXY_RESULT_ENTRY", "WINHTTP_PROXY_INFO", "URL_COMPONENTS",
    ];

    private static readonly SymbolDisplayFormat ApiDisplay = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers | SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderAnnotationCompatibilityTests"/> class.
    /// </summary>
    /// <param name="logger">The test output.</param>
    public HeaderAnnotationCompatibilityTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Compiles the same caller and compares public symbols across all three canonical legs.
    /// </summary>
    /// <param name="allowMarshaling">The pinned marshaling option.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanonicalThreeWayMatrix(bool allowMarshaling)
    {
        RequireBaseline();
        CSharpCompilation references = this.starterCompilations["net8.0-x64"];
        string oldPath = AssociatedEnumTests.EmitMetadata(AssociatedEnumTests.GetMetadataSource(true), references, this.parseOptions);
        string newPath = AssociatedEnumTests.EmitMetadata(AssociatedEnumTests.GetMetadataSource(false), references, this.parseOptions);
        try
        {
            string callers = GetCanonicalCaller(allowMarshaling);
            MatrixResult result = this.RunMatrix("canonical", oldPath, newPath, CanonicalRequests, callers, allowMarshaling);
            Assert.All(result.Runs, run => Assert.Empty(run.CallerDiagnostics));
            Assert.Equal(result.Runs[0].PublicSymbols, result.Runs[1].PublicSymbols);
            Assert.Equal(result.Runs[0].PublicSymbols, result.Runs[2].PublicSymbols);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    /// <summary>
    /// Proves direct native enum storage against OLD synthetic fields and signatures, even when logical storage differs.
    /// </summary>
    /// <param name="storage">The native integral storage.</param>
    /// <param name="allowMarshaling">The pinned marshaling option.</param>
    /// <param name="enumFirst">Whether to request the enum before its annotated consumers.</param>
    [Theory, CombinatorialData]
    public void NativeStorageThreeWayMatrix(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string storage,
        bool allowMarshaling,
        bool enumFirst)
    {
        RequireBaseline();
        CSharpCompilation references = this.starterCompilations["net8.0-x64"];
        string oldPath = AssociatedEnumTests.EmitMetadata(AssociatedEnumTests.GetNativeStorageMetadata(storage, legacy: true), references, this.parseOptions);
        string newPath = AssociatedEnumTests.EmitMetadata(AssociatedEnumTests.GetNativeStorageMetadata(storage, legacy: false), references, this.parseOptions);
        try
        {
            string[] requests = enumFirst ? ["MODE", .. CanonicalRequests] : CanonicalRequests;
            MatrixResult result = this.RunMatrix($"native-storage-{storage}-{(enumFirst ? "enum-first" : "consumer-first")}", oldPath, newPath, requests, GetCanonicalCaller(allowMarshaling), allowMarshaling);
            Assert.All(result.Runs, run => Assert.Empty(run.CallerDiagnostics));
            Assert.Equal(result.Runs[0].PublicSymbols, result.Runs[1].PublicSymbols);
            Assert.Equal(result.Runs[0].PublicSymbols, result.Runs[2].PublicSymbols);
            Assert.Contains(result.Runs[2].PublicSymbols, symbol => symbol.StartsWith("NamedType ", StringComparison.Ordinal) && symbol.Contains($"enum={storage}; flags=True", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    /// <summary>
    /// Records source-compatible field usage and the narrower property-contract break in existing annotated metadata.
    /// </summary>
    /// <param name="allowMarshaling">The pinned marshaling option.</param>
    /// <param name="requireProperty">Whether the unchanged partial struct implements a property contract.</param>
    [Theory, CombinatorialData]
    public void ExistingAnnotatedFieldRegression(bool allowMarshaling, bool requireProperty)
    {
        RequireBaseline();
        string source = AssociatedEnumTests.MetadataSource.Replace(
            "[AssociatedEnum(\"Windows.Win32.Test.MODE\")] public MODE_STORAGE* Modes;",
            string.Empty,
            StringComparison.Ordinal);
        string path = AssociatedEnumTests.EmitMetadata(source, this.starterCompilations["net8.0-x64"], this.parseOptions);
        string caller = OrdinaryFieldCaller + (requireProperty ? Environment.NewLine + PropertyInterfaceCaller : string.Empty);

        try
        {
            MatrixResult result = this.RunMatrix(requireProperty ? "existing-annotation-property-contract" : "existing-annotation-field", path, null, ["RECORD"], caller, allowMarshaling, compatibilityScope: "independent-existing-annotation-regression");
            Assert.Empty(result.Runs[0].CallerDiagnostics);
            foreach (RunResult run in result.Runs.Skip(1))
            {
                Assert.Contains(run.RemovedPublicSymbols, symbol => symbol.StartsWith("Property ", StringComparison.Ordinal) && symbol.Contains("RECORD.Mode", StringComparison.Ordinal));
                Assert.Contains(run.AddedPublicSymbols, symbol => symbol.StartsWith("Field ", StringComparison.Ordinal) && symbol.Contains("RECORD.Mode", StringComparison.Ordinal));
                Assert.Equal(requireProperty ? "residual-source-break" : "source-compatible-difference", run.Grade);
                if (requireProperty)
                {
                    string diagnostic = Assert.Single(run.CallerDiagnostics);
                    Assert.Contains("CS0535", diagnostic, StringComparison.Ordinal);
                    Assert.Contains("IModeProperty.Mode", diagnostic, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Empty(run.CallerDiagnostics);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Separates the frozen OLD constructor defect from a corrected, unmigrated tooling control.
    /// </summary>
    [Fact]
    public void FrozenOldConstructorLimitationsHaveCorrectedToolingControl()
    {
        string? oldPath = Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OLD_METADATA");
        string? controlPath = Environment.GetEnvironmentVariable("CSWIN32_COMPAT_TOOLING_CONTROL_METADATA");
        Assert.SkipWhen(string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(controlPath), "Supply the frozen OLD snapshot and a separately pinned unmigrated tooling control.");
        Assert.Equal(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OLD_SHA256"), HashFile(oldPath!));
        Assert.Equal(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_TOOLING_CONTROL_SHA256"), HashFile(controlPath!));
        string[] oldLimitations = MetadataConstructorLimitations(oldPath!);
        string[] controlLimitations = MetadataConstructorLimitations(controlPath!);
        Assert.Contains(oldLimitations, limitation => limitation.Contains("AssociatedEnumAttribute", StringComparison.Ordinal) && limitation.Contains("instance MemberRef present=True", StringComparison.Ordinal));
        Assert.Empty(controlLimitations);
        if (Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OUTPUT") is string output)
        {
            string report = JsonSerializer.Serialize(
                new
                {
                    ControlKind = "unmigrated SDK; physical constructor check only; not WinHttp NEW acceptance",
                    OldMetadataSHA256 = HashFile(oldPath!),
                    ControlMetadataSHA256 = HashFile(controlPath!),
                    OldMetadataPhysicalLimitations = oldLimitations,
                    ControlMetadataPhysicalLimitations = controlLimitations,
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(output, "unmigrated-tooling-control.json"), report);
        }
    }

    /// <summary>
    /// Runs the real SDK pilot only when both immutable snapshots and their hashes are supplied.
    /// </summary>
    /// <param name="allowMarshaling">The pinned marshaling option.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WinHttpThreeWayMatrix(bool allowMarshaling)
    {
        RequireBaseline();
        string? oldPath = Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OLD_METADATA");
        string? newPath = Environment.GetEnvironmentVariable("CSWIN32_COMPAT_NEW_METADATA");
        Assert.SkipWhen(string.IsNullOrEmpty(oldPath), "Run tools\\Test-HeaderAnnotationCompatibility.ps1 with the pinned WinHttp snapshots.");
        Assert.Equal(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OLD_SHA256"), HashFile(oldPath!));
        if (!string.IsNullOrEmpty(newPath))
        {
            Assert.Equal(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_NEW_SHA256"), HashFile(newPath));
        }

        // The full caller corpus is shared verbatim between the three legs.
        string callers = """
            using System;
            using System.Runtime.InteropServices;
            using Windows.Win32;
            using Windows.Win32.Foundation;
            using Windows.Win32.Networking.WinHttp;
            [assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
            static unsafe class WinHttpCaller
            {
                static void EnumFields(WINHTTP_PROXY_INFO proxy, WINHTTP_CREDS credentials, WINHTTP_CREDS_EX extended, URL_COMPONENTS url, WINHTTP_PROXY_RESULT_ENTRY entry)
                {
                    proxy.dwAccessType = WINHTTP_ACCESS_TYPE.WINHTTP_ACCESS_TYPE_NO_PROXY;
                    ref WINHTTP_ACCESS_TYPE access = ref proxy.dwAccessType;
                    credentials.dwAuthScheme = WINHTTP_CREDS_AUTHSCHEME.WINHTTP_AUTH_SCHEME_BASIC;
                    extended.dwAuthScheme = WINHTTP_CREDS_AUTHSCHEME.WINHTTP_AUTH_SCHEME_NEGOTIATE;
                    ref WINHTTP_CREDS_AUTHSCHEME auth = ref credentials.dwAuthScheme;
                    url.nScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_HTTP;
                    url.nScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_HTTPS;
                    entry.ProxyScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_FTP;
                    entry.ProxyScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_SOCKS;
                    ref WINHTTP_INTERNET_SCHEME scheme = ref url.nScheme;
                }
                static void Raw(PCWSTR agent, PCWSTR server, PCWSTR verb, PCWSTR path, void* data, WINHTTP_AUTOPROXY_OPTIONS* autoProxy)
                {
                    void* session = PInvoke.WinHttpOpen(agent, WINHTTP_ACCESS_TYPE.WINHTTP_ACCESS_TYPE_NO_PROXY, default(PCWSTR), default(PCWSTR), 0);
                    void* connection = PInvoke.WinHttpConnect(session, server, 443, 0);
                    WINHTTP_OPEN_REQUEST_FLAGS flags = WINHTTP_OPEN_REQUEST_FLAGS.WINHTTP_FLAG_SECURE | WINHTTP_OPEN_REQUEST_FLAGS.WINHTTP_FLAG_REFRESH;
                    void* request = PInvoke.WinHttpOpenRequest(connection, verb, path, default(PCWSTR), default(PCWSTR), null, flags);
                    uint read;
                    PInvoke.WinHttpReadData(request, data, 16, &read);
                    WINHTTP_PROXY_INFO proxy;
                    PInvoke.WinHttpGetProxyForUrl(session, path, autoProxy, &proxy);
                    void* resolver;
                    PInvoke.WinHttpCreateProxyResolver(session, &resolver);
                    URL_COMPONENTS components = default;
                    PInvoke.WinHttpCrackUrl(path, 0, 0, &components);
                    uint length = 0;
                    WIN_HTTP_CREATE_URL_FLAGS createFlags = WIN_HTTP_CREATE_URL_FLAGS.ICU_ESCAPE | WIN_HTTP_CREATE_URL_FLAGS.ICU_REJECT_USERPWD;
                    PInvoke.WinHttpCreateUrl(&components, createFlags, default(PWSTR), &length);
                    uint index = 0, headerCount = 0, bufferLength = 16;
                    PInvoke.WinHttpQueryHeadersEx(request, 0, 0, 0, &index, null, data, &bufferLength, null, &headerCount);
                    PInvoke.WinHttpCloseHandle(resolver);
                    PInvoke.WinHttpCloseHandle(request);
                    PInvoke.WinHttpCloseHandle(connection);
                    PInvoke.WinHttpCloseHandle(session);
                }
                static void Friendly(void* request, byte* data)
                {
                    void* session = PInvoke.WinHttpOpen("compatibility", WINHTTP_ACCESS_TYPE.WINHTTP_ACCESS_TYPE_NO_PROXY, null, null, 0);
                    void* connection = PInvoke.WinHttpConnect(session, "example.invalid", 443, 0);
                    uint read = 0;
                    PInvoke.WinHttpReadData(request, new Span<byte>(data, 16), ref read);
                    PInvoke.WinHttpCreateProxyResolver(session, out void* resolver);
                }
                /*CALLBACK*/
            }
            """;
        string callback = allowMarshaling
            ? """
                static void Register(void* request) => PInvoke.WinHttpSetStatusCallback(request, Callback, 0, 0);
                static void Callback(void* handle, nuint context, uint status, void* info, uint length) { }
                """
            : """
                static void Register(void* request) => PInvoke.WinHttpSetStatusCallback(request, &Callback, 0, 0);
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                static void Callback(void* handle, nuint context, uint status, void* info, uint length) { }
                """;
        callers = callers.Replace("/*CALLBACK*/", callback, StringComparison.Ordinal);
        MatrixResult result = this.RunMatrix("winhttp", oldPath!, newPath, WinHttpRequests, callers, allowMarshaling);
        Assert.Empty(result.Runs[0].CallerDiagnostics);
        Assert.Empty(result.Runs[1].CallerDiagnostics);
        Assert.Equal(result.Runs[0].PublicSymbols, result.Runs[1].PublicSymbols);
        Assert.SkipWhen(string.IsNullOrEmpty(newPath), "NEW WinHttp metadata is pending; the OLD baseline/proposed pair was checked and reported.");
        Assert.Equal(4, result.Runs[2].CallerDiagnostics.Length);
        Assert.All(result.Runs[2].CallerDiagnostics, diagnostic =>
        {
            Assert.Contains("CS0117", diagnostic, StringComparison.Ordinal);
            Assert.Contains("WINHTTP_INTERNET_SCHEME_", diagnostic, StringComparison.Ordinal);
        });
        string adjustedCaller = callers.Replace("WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_", "WINHTTP_INTERNET_SCHEME.INTERNET_SCHEME_", StringComparison.Ordinal);
        RunResult adjusted = this.Generate("new-proposed-adjusted", newPath!, WinHttpRequests, adjustedCaller, result.Options, baseline: false);
        Assert.Empty(adjusted.CallerDiagnostics);
    }

    private static string Grade(RunResult baseline, RunResult proposed) => proposed.CallerDiagnostics.Length > 0 ? "residual-source-break"
        : proposed.GeneratedCode == baseline.GeneratedCode ? "identical-generation" : "source-compatible-difference";

    private static void RequireBaseline()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_BASELINE_DLL")), "Run tools\\Test-HeaderAnnotationCompatibility.ps1 to build the pinned baseline.");
        Assert.Matches("^[0-9a-f]{40}$", Environment.GetEnvironmentVariable("CSWIN32_COMPAT_BASELINE_COMMIT") ?? string.Empty);
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string GetCanonicalCaller(bool allowMarshaling)
    {
        string callback = allowMarshaling
            ? "static void Register() => PInvoke.UseCallback(Callback); static MODE Callback(MODE mode, MODE* data) => mode;"
            : "static void Register() => PInvoke.UseCallback(&Callback); [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] static MODE Callback(MODE mode, MODE* data) => mode;";
        return AssociatedEnumTests.Caller + Environment.NewLine + "namespace CallbackFixture { using System.Runtime.InteropServices; using Windows.Win32; using Windows.Win32.Test; static unsafe class CallbackCaller { " + callback + " } }";
    }

    private static string[] MetadataConstructorLimitations(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        HashSet<string> instanceReferences = new(StringComparer.Ordinal);
        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference reference = reader.GetMemberReference(handle);
            if (reference.Parent.Kind == HandleKind.TypeReference && reader.StringComparer.Equals(reference.Name, ".ctor")
                && reader.GetBlobReader(reference.Signature).ReadSignatureHeader().IsInstance)
            {
                TypeReference type = reader.GetTypeReference((TypeReferenceHandle)reference.Parent);
                instanceReferences.Add($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}");
            }
        }

        List<string> limitations = new();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (!reader.StringComparer.Equals(type.Namespace, "Windows.Win32.Foundation.Metadata"))
            {
                continue;
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (reader.StringComparer.Equals(method.Name, ".ctor") && !reader.GetBlobReader(method.Signature).ReadSignatureHeader().IsInstance)
                {
                    string name = $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
                    limitations.Add($"{name}..ctor MethodDef lacks HASTHIS; signature={Convert.ToHexString(reader.GetBlobBytes(method.Signature))}; instance MemberRef present={instanceReferences.Contains(name)}.");
                }
            }
        }

        return limitations.ToArray();
    }

    private static string[] PublicSymbols(CSharpCompilation compilation)
    {
        List<string> symbols = new();
        void Visit(INamespaceOrTypeSymbol container)
        {
            foreach (ISymbol member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    Visit(ns);
                }
                else if (member.DeclaredAccessibility == Accessibility.Public && !member.IsImplicitlyDeclared)
                {
                    string detail = member switch
                    {
                        INamedTypeSymbol type => $" : {type.BaseType?.ToDisplayString(ApiDisplay)}; enum={type.EnumUnderlyingType?.ToDisplayString(ApiDisplay)}; flags={type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute")}",
                        IFieldSymbol { HasConstantValue: true } field => $" = {Convert.ToString(field.ConstantValue, System.Globalization.CultureInfo.InvariantCulture)}",
                        _ => string.Empty,
                    };
                    symbols.Add($"{member.Kind} {member.ToDisplayString(ApiDisplay)}{detail}");
                    if (member is INamedTypeSymbol nested)
                    {
                        Visit(nested);
                    }
                }
            }
        }

        Visit(compilation.Assembly.GlobalNamespace);
        return symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray();
    }

    private MatrixResult RunMatrix(string name, string oldMetadata, string? newMetadata, string[] requests, string callers, bool allowMarshaling, string compatibilityScope = "synthetic-enum-migration")
    {
        callers = TestUtils.NormalizeToExpectedLineEndings(callers);
        GeneratorOptions options = DefaultTestGeneratorOptions with { Public = true, AllowMarshaling = allowMarshaling, UseSafeHandles = true };
        RunResult oldBaseline = this.Generate("old-baseline", oldMetadata, requests, callers, options, baseline: true);
        RunResult oldProposed = this.Generate("old-proposed", oldMetadata, requests, callers, options, baseline: false);
        RunResult[] runs = string.IsNullOrEmpty(newMetadata) ? [oldBaseline, oldProposed]
            : [oldBaseline, oldProposed, this.Generate("new-proposed", newMetadata!, requests, callers, options, baseline: false)];
        runs = runs.Select(run => run with
        {
            Grade = Grade(oldBaseline, run),
            RemovedPublicSymbols = oldBaseline.PublicSymbols.Except(run.PublicSymbols, StringComparer.Ordinal).ToArray(),
            AddedPublicSymbols = run.PublicSymbols.Except(oldBaseline.PublicSymbols, StringComparer.Ordinal).ToArray(),
        }).ToArray();
        MatrixResult result = new(
            compatibilityScope,
            Environment.GetEnvironmentVariable("CSWIN32_COMPAT_BASELINE_COMMIT")!,
            HashFile(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_BASELINE_DLL")!),
            Environment.GetEnvironmentVariable("CSWIN32_COMPAT_PROPOSED_COMMIT") ?? "uncommitted",
            HashFile(typeof(Generator).Assembly.Location),
            typeof(CSharpCompilation).Assembly.GetName().Version!.ToString(),
            "net8.0-windows; x64; unsafe; C# 12",
            options,
            requests,
            HashFile(oldMetadata),
            string.IsNullOrEmpty(newMetadata) ? null : HashFile(newMetadata!),
            callers,
            runs,
            MetadataConstructorLimitations(oldMetadata),
            string.IsNullOrEmpty(newMetadata) ? [] : MetadataConstructorLimitations(newMetadata!));
        string? output = Environment.GetEnvironmentVariable("CSWIN32_COMPAT_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            string prefix = Path.Combine(output, $"{name}-{(allowMarshaling ? "marshaling" : "no-marshaling")}");
            File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            foreach (RunResult run in result.Runs)
            {
                File.WriteAllText(prefix + "-" + run.Name + ".g.cs", run.GeneratedCode);
            }
        }

        foreach (RunResult run in result.Runs)
        {
            this.logger.WriteLine($"{name}/{allowMarshaling}/{run.Name}: {run.Grade}; {run.PublicSymbols.Length} public symbols; {run.CallerDiagnostics.Length} caller diagnostics.");
        }

        Assert.Empty(result.NewMetadataPhysicalLimitations);
        return result;
    }

    private RunResult Generate(string name, string metadata, string[] requests, string callers, GeneratorOptions options, bool baseline)
    {
        CSharpCompilation references = this.starterCompilations["net8.0-x64"];
        IEnumerable<KeyValuePair<string, CompilationUnitSyntax>> units;
        if (baseline)
        {
            using BaselineGenerator generator = new(metadata, options, references, this.parseOptions);
            foreach (string request in requests)
            {
                generator.Generate(request);
            }

            units = generator.GetCompilationUnits().ToArray();
        }
        else
        {
            using Generator generator = new(metadata, null, [], options, references, this.parseOptions);
            foreach (string request in requests)
            {
                Assert.True(generator.TryGenerate(request, TestContext.Current.CancellationToken));
            }

            units = generator.GetCompilationUnits(TestContext.Current.CancellationToken).ToArray();
        }

        string code = string.Join(Environment.NewLine, units.OrderBy(unit => unit.Key, StringComparer.Ordinal).Select(unit => unit.Value.ToFullString()));
        CSharpCompilation generated = references.AddSyntaxTrees(units.Select(unit => CSharpSyntaxTree.ParseText(unit.Value.ToFullString(), this.parseOptions, unit.Key)));
        this.AssertNoDiagnostics(generated, logAllGeneratedCode: false);
        string[] symbols = PublicSymbols(generated);
        CSharpCompilation withCallers = generated.AddSyntaxTrees(CSharpSyntaxTree.ParseText(callers, this.parseOptions, "UnchangedCaller.cs"));
        string[] diagnostics = withCallers.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.ToString()).ToArray();
        if (diagnostics.Length == 0)
        {
            this.AssertNoDiagnostics(withCallers, logAllGeneratedCode: false);
        }

        return new(name, code, symbols, diagnostics, "ungraded", [], []);
    }

    private sealed record RunResult(string Name, string GeneratedCode, string[] PublicSymbols, string[] CallerDiagnostics, string Grade, string[] RemovedPublicSymbols, string[] AddedPublicSymbols);

    private sealed record MatrixResult(
        string CompatibilityScope,
        string BaselineCommit,
        string BaselineAssemblySHA256,
        string ProposedCommit,
        string ProposedAssemblySHA256,
        string RoslynVersion,
        string Compilation,
        GeneratorOptions Options,
        string[] Requests,
        string OldMetadataSHA256,
        string? NewMetadataSHA256,
        string UnchangedCaller,
        RunResult[] Runs,
        string[] OldMetadataPhysicalLimitations,
        string[] NewMetadataPhysicalLimitations);

    private sealed class BaselineGenerator : IDisposable
    {
        private readonly AssemblyLoadContext context = new("CsWin32 compatibility baseline", isCollectible: true);
        private readonly object generator;
        private readonly Type generatorType;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaselineGenerator"/> class.
        /// </summary>
        /// <param name="metadata">The pinned metadata snapshot.</param>
        /// <param name="options">The shared generation options.</param>
        /// <param name="compilation">The shared reference compilation.</param>
        /// <param name="parseOptions">The shared language options.</param>
        internal BaselineGenerator(string metadata, GeneratorOptions options, CSharpCompilation compilation, CSharpParseOptions parseOptions)
        {
            Assembly assembly = this.context.LoadFromAssemblyPath(Path.GetFullPath(Environment.GetEnvironmentVariable("CSWIN32_COMPAT_BASELINE_DLL")!));
            this.generatorType = assembly.GetType(typeof(Generator).FullName!, throwOnError: true)!;
            Type optionsType = assembly.GetType(typeof(GeneratorOptions).FullName!, throwOnError: true)!;
            object baselineOptions = JsonSerializer.Deserialize(JsonSerializer.Serialize(options), optionsType)!;
            this.generator = Activator.CreateInstance(this.generatorType, [metadata, null, Array.Empty<string>(), baselineOptions, compilation, parseOptions])!;
        }

        public void Dispose()
        {
            ((IDisposable)this.generator).Dispose();
            this.context.Unload();
        }

        /// <summary>
        /// Generates one requested API using the isolated baseline assembly.
        /// </summary>
        /// <param name="api">The requested API.</param>
        internal void Generate(string api)
        {
            MethodInfo method = this.generatorType.GetMethod("TryGenerate", [typeof(string), typeof(IReadOnlyCollection<string>).MakeByRefType(), typeof(CancellationToken)])!;
            Assert.Equal(true, method.Invoke(this.generator, [api, null, TestContext.Current.CancellationToken]));
        }

        /// <summary>
        /// Gets the baseline-generated syntax trees.
        /// </summary>
        /// <returns>The generated compilation units.</returns>
        internal IEnumerable<KeyValuePair<string, CompilationUnitSyntax>> GetCompilationUnits() =>
            (IEnumerable<KeyValuePair<string, CompilationUnitSyntax>>)this.generatorType.GetMethod("GetCompilationUnits")!.Invoke(this.generator, [TestContext.Current.CancellationToken])!;
    }
}
