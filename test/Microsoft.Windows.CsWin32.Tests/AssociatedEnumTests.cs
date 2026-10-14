// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Verifies logical enums without substituting them into native metadata signatures.
/// </summary>
public class AssociatedEnumTests : GeneratorTestBase
{
    /// <summary>
    /// The canonical metadata declarations, using native primitives with logical enum associations.
    /// </summary>
    internal const string MetadataSource = """
        using System;
        using System.Runtime.InteropServices;
        using Windows.Win32.Foundation.Metadata;
        using MODE_STORAGE = System.UInt32;

        namespace Windows.Win32.Foundation.Metadata
        {
            public sealed class AssociatedEnumAttribute : Attribute
            {
                public AssociatedEnumAttribute(string name) { }
            }
            public sealed class NativeArrayInfoAttribute : Attribute
            {
                public short CountParamIndex;
                public int CountConst;
            }
        }
        namespace Windows.Win32.Test
        {
            [Flags]
            public enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }
            public unsafe struct RECORD
            {
                [AssociatedEnum("MODE")] public MODE_STORAGE Mode;
                [AssociatedEnum("Windows.Win32.Test.MODE")] public MODE_STORAGE* Modes;
            }
            public unsafe struct FIXED_RECORD
            {
                [AssociatedEnum("MODE")] public fixed uint Modes[2];
            }
            public struct NARROW_RECORD
            {
                [AssociatedEnum("MODE")] public ushort Mode;
                public ushort Tail;
            }
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            [return: AssociatedEnum("MODE")]
            public unsafe delegate MODE_STORAGE CALLBACK([AssociatedEnum("MODE")] MODE_STORAGE mode, [AssociatedEnum("MODE")] MODE_STORAGE* data);
            public static unsafe class Apis
            {
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: AssociatedEnum("MODE")]
                public static extern MODE_STORAGE RoundTrip([AssociatedEnum("MODE")] MODE_STORAGE mode);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: AssociatedEnum("Windows.Win32.Test.MODE")]
                public static extern MODE_STORAGE* PointerReturn();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void Buffer([In, AssociatedEnum("MODE"), NativeArrayInfo(CountParamIndex = 1)] MODE_STORAGE* modes, uint count);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void Output([Out, AssociatedEnum("MODE")] MODE_STORAGE* mode);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void DoublePointer([Out, AssociatedEnum("MODE")] MODE_STORAGE** modes);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void UseCallback(CALLBACK callback);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void Narrow([AssociatedEnum("MODE")] ushort mode);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: AssociatedEnum("MODE")]
                public static extern ushort NarrowReturn();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void NarrowPointer([AssociatedEnum("MODE")] ushort* mode);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void Unannotated(uint mode);
            }
        }
        """;

    /// <summary>
    /// The unchanged caller shared by the canonical compatibility legs and mutation controls.
    /// </summary>
    internal const string Caller = """
        using System;
        using System.Runtime.InteropServices;
        using Windows.Win32;
        using Windows.Win32.Test;
        static unsafe class Caller
        {
            static void Use(MODE* modes, MODE** indirect, RECORD record)
            {
                MODE flags = MODE.First | MODE.Second | MODE.High;
                MODE result = PInvoke.RoundTrip(flags);
                record.Mode = flags;
                ref MODE field = ref record.Mode;
                MODE* fields = record.Modes;
                PInvoke.Buffer(modes, 2);
                PInvoke.Buffer(new ReadOnlySpan<MODE>(modes, 2));
                PInvoke.Output(modes);
                PInvoke.Output(out MODE output);
                PInvoke.DoublePointer(indirect);
                MODE* returned = PInvoke.PointerReturn();
                PInvoke.Unannotated(42);
            }
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssociatedEnumTests"/> class.
    /// </summary>
    /// <param name="logger">The test output.</param>
    public AssociatedEnumTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Compiles unchanged enum, flags, ref-field, buffer and pointer callers against old and new metadata.
    /// </summary>
    /// <param name="legacy">Whether enums occur directly in the native metadata signatures.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory, CombinatorialData]
    public void NativeSignaturesRecoverLogicalEnumCallers(bool legacy, bool allowMarshaling)
    {
        this.GenerateFixture(legacy, allowMarshaling, "RECORD", "RoundTrip", "PointerReturn", "Buffer", "Output", "DoublePointer", "UseCallback", "Unannotated");
        this.compilation = this.AddCode(Caller);
        string callback = allowMarshaling
            ? "static void Register() => PInvoke.UseCallback(Callback); static MODE Callback(MODE mode, MODE* data) => mode;"
            : "static void Register() => PInvoke.UseCallback(&Callback); [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] static MODE Callback(MODE mode, MODE* data) => mode;";
        this.compilation = this.AddCode("using System.Runtime.InteropServices; using Windows.Win32; using Windows.Win32.Test; static unsafe class CallbackCaller { " + callback + " }");
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        INamedTypeSymbol record = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.RECORD")!;
        Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(record.GetMembers("Mode")));
        Assert.Equal("Windows.Win32.Test.MODE", this.compilation.GetTypeByMetadataName("Windows.Win32.Test.MODE")!.ToDisplayString());
        Assert.All(this.FindGeneratedMethod("DoublePointer"), method => Assert.Contains("MODE*", method.ParameterList.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies exact storage for all legal enum underlying types, independent of marshaling and CPU.
    /// </summary>
    /// <param name="storage">The native and enum storage type.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="platform">The target CPU.</param>
    [Theory, CombinatorialData]
    public void ExactIntegralStorage(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string storage,
        bool allowMarshaling,
        [CombinatorialValues(Platform.X86, Platform.X64, Platform.Arm64)] Platform platform)
    {
        string systemType = storage switch
        {
            "sbyte" => "SByte", "byte" => "Byte", "short" => "Int16", "ushort" => "UInt16",
            "int" => "Int32", "uint" => "UInt32", "long" => "Int64", _ => "UInt64",
        };
        string source = MetadataSource.Replace("using MODE_STORAGE = System.UInt32;", $"using MODE_STORAGE = System.{systemType};", StringComparison.Ordinal)
            .Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", $"enum MODE : {storage} {{ First = 1, Second = 2, High = 4 }}", StringComparison.Ordinal);
        this.GenerateSource(source, allowMarshaling, platform, "RECORD", "RoundTrip", "PointerReturn", "Output", "DoublePointer", "Buffer", "Unannotated");
        this.compilation = this.AddCode(Caller);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        IMethodSymbol roundTrip = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>());
        Assert.Equal(storage, ((INamedTypeSymbol)roundTrip.ReturnType).EnumUnderlyingType!.ToDisplayString());
    }

    /// <summary>
    /// Verifies an inline enum buffer and a converting scalar property without changing native layout.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FieldStorageAndFixedBuffers(bool allowMarshaling)
    {
        this.GenerateFixture(false, allowMarshaling, "FIXED_RECORD", "NARROW_RECORD", "Narrow");
        this.compilation = this.AddCode("""
            using Windows.Win32;
            using Windows.Win32.Test;
            /// <summary>Exercises native field storage.</summary>
            public static unsafe class Layout
            {
                /// <summary>Returns both layout sizes.</summary>
                public static int Run()
                {
                    FIXED_RECORD record = default;
                    record.Modes.AsSpan()[0] = MODE.First | MODE.High;
                    NARROW_RECORD narrow = default;
                    checked { narrow.Mode = MODE.High | MODE.Second; }
                    if (record.Modes.AsSpan()[0] != (MODE.First | MODE.High) || narrow.Mode != MODE.Second) throw new System.Exception();
                    return sizeof(FIXED_RECORD) * 100 + sizeof(NARROW_RECORD);
                }
                static void Compile() => PInvoke.Narrow(MODE.Second);
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        using MemoryStream stream = new();
        var emitted = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(804, assembly.GetType("Layout")!.GetMethod("Run")!.Invoke(null, null));
    }

    /// <summary>
    /// Verifies that unsupported associations fail instead of producing native-only or invalid projections.
    /// </summary>
    /// <param name="replacement">The invalid association or native shape.</param>
    /// <param name="api">The requested API.</param>
    /// <param name="message">The expected diagnostic detail.</param>
    [Theory]
    [InlineData("Missing", "RoundTrip", "was not found")]
    [InlineData("RECORD", "RoundTrip", "does not refer to an enum")]
    [InlineData("", "RoundTrip", "nonempty enum name")]
    [InlineData("MODE", "NarrowPointer", "same width and signedness")]
    public void InvalidAssociationsFailExplicitly(string replacement, string api, string message)
    {
        string source = MetadataSource.Replace("AssociatedEnum(\"MODE\")", $"AssociatedEnum(\"{replacement}\")", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, api));
        Assert.Contains(message, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies declaring-namespace preference and explicit ambiguity errors.
    /// </summary>
    [Fact]
    public void NamespaceResolutionAndAmbiguity()
    {
        const string OtherEnums = "namespace Windows.Win32.Other { public enum MODE : uint { Other = 1 } } namespace Windows.Win32.Another { public enum MODE : uint { Another = 1 } }";
        this.GenerateSource(MetadataSource + OtherEnums, false, Platform.X64, "RoundTrip");
        Assert.Equal("Windows.Win32.Test.MODE", Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>()).ReturnType.ToDisplayString());
        this.generator!.Dispose();
        this.generator = null;
        string source = MetadataSource.Replace("enum MODE : uint", "enum LOCAL_MODE : uint", StringComparison.Ordinal) + OtherEnums;
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, "RoundTrip"));
        Assert.Contains("ambiguous", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves annotations through actual assembly references, not arbitrary loaded metadata.
    /// </summary>
    /// <param name="referenced">Whether the native metadata references the enum's assembly.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QualifiedEnumReferenceClosure(bool referenced)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string external = EmitMetadata("namespace Windows.Win32.External { public enum EXTERNAL_MODE : uint { First = 1 } }", this.compilation, this.parseOptions, "Windows.Win32.External.winmd");
        string source = MetadataSource.Replace("AssociatedEnum(\"MODE\")", "AssociatedEnum(\"Windows.Win32.External.EXTERNAL_MODE\")", StringComparison.Ordinal);
        CSharpCompilation references = this.compilation;
        if (referenced)
        {
            source += "namespace Windows.Win32.Test { public struct ANCHOR { public Windows.Win32.External.EXTERNAL_MODE Value; } }";
            references = references.AddReferences(MetadataReference.CreateFromFile(external));
        }

        string native = EmitMetadata(source, references, this.parseOptions);
        try
        {
            GeneratorOptions options = DefaultTestGeneratorOptions with { Public = true, AllowMarshaling = false };
            this.generator = SuperGenerator.Combine(
                new Generator(native, null, [], options, this.compilation, this.parseOptions),
                new Generator(external, null, [], options, this.compilation, this.parseOptions));
            if (referenced)
            {
                Assert.True(this.generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
                this.CollectGeneratedCode(this.generator);
                this.compilation = this.AddCode("static class ExternalCaller { static void Use() => Windows.Win32.PInvoke.RoundTrip(Windows.Win32.External.EXTERNAL_MODE.First); }");
                this.AssertNoDiagnostics(logAllGeneratedCode: false);
            }
            else
            {
                GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
                Assert.Contains("metadata reference closure", exception.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            this.generator?.Dispose();
            this.generator = null;
            File.Delete(native);
            File.Delete(external);
        }
    }

    /// <summary>
    /// Verifies fixed-rank metadata arrays as well as their native storage guard.
    /// </summary>
    [Fact]
    public void MetadataArrayElements()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "AssociatedEnumArrays.winmd");
        this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true, AllowMarshaling = false }, this.compilation, this.parseOptions);
        Assert.True(this.generator.TryGenerate("ARRAY_RECORD", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("ARRAY_FLEX", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);
        this.compilation = this.AddCode("""
            using Windows.Win32.Test;
            /// <summary>Exercises metadata fixed-rank arrays.</summary>
            public static unsafe class ArrayLayout
            {
                /// <summary>Returns fixed and flexible layout sizes.</summary>
                public static int Run()
                {
                    ARRAY_RECORD record = default;
                    record.Modes.AsSpan()[0] = ARRAY_MODE.First;
                    if (record.Modes.AsSpan()[0] != ARRAY_MODE.First) throw new System.Exception();
                    return sizeof(ARRAY_RECORD) * 100 + ARRAY_FLEX.SizeOf(3);
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        using MemoryStream stream = new();
        var emitted = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Equal(812, Assembly.Load(stream.ToArray()).GetType("ArrayLayout")!.GetMethod("Run")!.Invoke(null, null));
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.generator.TryGenerate("BAD_ARRAY_RECORD", TestContext.Current.CancellationToken));
        Assert.Contains("same width and signedness", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Covers each native conversion supported by source-generated marshaling.
    /// </summary>
    /// <param name="native">The native storage code.</param>
    [Theory, CombinatorialData]
    public void SourceGeneratedIntegralConversions(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string native)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string source = native == "uint"
            ? MetadataSource.Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", "enum MODE : int { First = 1, Second = 2, High = -2147483648 }", StringComparison.Ordinal)
            : MetadataSource;
        source = source.Replace("ushort mode", native + " mode", StringComparison.Ordinal).Replace("ushort NarrowReturn", native + " NarrowReturn", StringComparison.Ordinal);
        string path = EmitMetadata(source, this.compilation, this.parseOptions);
        try
        {
            Generator generator = new(path, null, [], DefaultTestGeneratorOptions with { Public = true, ComInterop = new() { UseComSourceGenerators = true } }, this.compilation, this.parseOptions);
            this.generator = generator;
            Assert.True(generator.TryGenerate("Narrow", TestContext.Current.CancellationToken));
            Assert.True(generator.TryGenerate("NarrowReturn", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(generator);
            INamedTypeSymbol pinvoke = this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!;
            IMethodSymbol method = Assert.Single(pinvoke.GetMembers("Narrow").OfType<IMethodSymbol>());
            AttributeData marshalUsing = Assert.Single(method.Parameters[0].GetAttributes(), attribute => attribute.AttributeClass?.Name == "MarshalUsingAttribute");
            INamedTypeSymbol marshaler = Assert.IsAssignableFrom<INamedTypeSymbol>(marshalUsing.ConstructorArguments[0].Value);
            IMethodSymbol convert = Assert.Single(marshaler.GetMembers("ConvertToUnmanaged").OfType<IMethodSymbol>());
            Assert.Equal(native, convert.ReturnType.ToDisplayString());
            IMethodSymbol returning = Assert.Single(pinvoke.GetMembers("NarrowReturn").OfType<IMethodSymbol>());
            AttributeData returnAttribute = Assert.Single(returning.GetReturnTypeAttributes(), attribute => attribute.AttributeClass?.Name == "MarshalUsingAttribute");
            Assert.True(SymbolEqualityComparer.Default.Equals(marshaler, returnAttribute.ConstructorArguments[0].Value as ITypeSymbol));
            this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false, acceptable: diagnostic => diagnostic.Id == "CS8795");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Rejects unsupported source-generated indirect conversions rather than dropping the element marshaler.
    /// </summary>
    /// <param name="attributes">The pointer's direction and array annotations.</param>
    [Theory]
    [InlineData("")]
    [InlineData("In, ")]
    [InlineData("Out, ")]
    [InlineData("In, Out, ")]
    [InlineData("In, NativeArrayInfo(CountConst = 2), ")]
    public void SourceGeneratedIndirectConversionsFailExplicitly(string attributes)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string source = MetadataSource.Replace("void NarrowPointer([AssociatedEnum", "void NarrowPointer([" + attributes + "AssociatedEnum", StringComparison.Ordinal);
        string path = EmitMetadata(source, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true, ComInterop = new() { UseComSourceGenerators = true } }, this.compilation, this.parseOptions);
            GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.generator.TryGenerate("NarrowPointer", TestContext.Current.CancellationToken));
            Assert.Contains("same width and signedness", exception.ToString(), StringComparison.Ordinal);
            Assert.True(this.generator.TryGenerate("Buffer", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(this.generator);
            IMethodSymbol buffer = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("Buffer").OfType<IMethodSymbol>(), method => method.Parameters.Length == 2);
            IPointerTypeSymbol pointer = Assert.IsAssignableFrom<IPointerTypeSymbol>(buffer.Parameters[0].Type);
            Assert.Equal("Windows.Win32.Test.MODE", pointer.PointedAtType.ToDisplayString());
            this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false, acceptable: diagnostic => diagnostic.Id == "CS8795");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Rejects compound bitfield associations whose arithmetic requires native primitive backing storage.
    /// </summary>
    [Fact]
    public void BitfieldAssociationFailsExplicitly()
    {
        string source = MetadataSource.Replace(
            "public sealed class NativeArrayInfoAttribute",
            "public sealed class NativeBitfieldAttribute : Attribute { public NativeBitfieldAttribute(string name, long offset, long length) { } } public sealed class NativeArrayInfoAttribute",
            StringComparison.Ordinal)
            .Replace("[AssociatedEnum(\"MODE\")] public MODE_STORAGE Mode;", "[AssociatedEnum(\"MODE\"), NativeBitfield(\"FirstBit\", 0, 1)] public MODE_STORAGE Mode;", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, "RECORD"));
        Assert.Contains("NativeBitfield", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that the caller fixture actually detects enum and pointer-depth regressions.
    /// </summary>
    /// <param name="mutation">The broken metadata signature.</param>
    [Theory]
    [InlineData("remove-association")]
    [InlineData("pointer-depth")]
    public void CallerMutationControls(string mutation)
    {
        string source = mutation == "remove-association"
            ? MetadataSource.Replace("[AssociatedEnum(\"MODE\")] MODE_STORAGE mode", "MODE_STORAGE mode", StringComparison.Ordinal)
            : MetadataSource.Replace("MODE_STORAGE** modes", "MODE_STORAGE* modes", StringComparison.Ordinal);
        this.GenerateSource(source, false, Platform.X64, "RECORD", "RoundTrip", "PointerReturn", "Buffer", "Output", "DoublePointer", "Unannotated");
        this.compilation = this.AddCode(Caller);
        Assert.Contains(this.compilation.GetDiagnostics(TestContext.Current.CancellationToken), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree?.FilePath.StartsWith("AdditionalCode", StringComparison.Ordinal) is true);
    }

    /// <summary>
    /// Creates the pinned canonical old or new metadata source.
    /// </summary>
    /// <param name="legacy">Whether enum types replace native primitives.</param>
    /// <returns>The metadata source.</returns>
    internal static string GetMetadataSource(bool legacy) => legacy
        ? MetadataSource.Replace("using MODE_STORAGE = System.UInt32;", "using MODE_STORAGE = Windows.Win32.Test.MODE;", StringComparison.Ordinal)
            .Replace("[AssociatedEnum(\"MODE\")]", string.Empty, StringComparison.Ordinal)
            .Replace("[AssociatedEnum(\"Windows.Win32.Test.MODE\")]", string.Empty, StringComparison.Ordinal)
            .Replace("[return: AssociatedEnum(\"MODE\")]", string.Empty, StringComparison.Ordinal)
            .Replace("[return: AssociatedEnum(\"Windows.Win32.Test.MODE\")]", string.Empty, StringComparison.Ordinal)
            .Replace("AssociatedEnum(\"MODE\"), ", string.Empty, StringComparison.Ordinal)
            .Replace(", AssociatedEnum(\"MODE\")", string.Empty, StringComparison.Ordinal)
        : MetadataSource;

    /// <summary>
    /// Emits a deterministic canonical metadata snapshot.
    /// </summary>
    /// <param name="source">The declarations and annotation definitions.</param>
    /// <param name="references">The pinned reference compilation.</param>
    /// <param name="parseOptions">The language options.</param>
    /// <param name="assemblyName">The metadata assembly identity.</param>
    /// <returns>The path of the snapshot, owned by the caller.</returns>
    internal static string EmitMetadata(string source, CSharpCompilation references, CSharpParseOptions parseOptions, string assemblyName = "Windows.Win32.winmd")
    {
        CSharpCompilation metadata = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references.References,
            references.Options.WithDeterministic(true).WithPlatform(Platform.AnyCpu));
        string path = Path.Combine(Path.GetTempPath(), $"CsWin32-AssociatedEnum-{Guid.NewGuid():N}.winmd");
        using FileStream stream = File.Create(path);
        var result = metadata.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }

    private void GenerateFixture(bool legacy, bool allowMarshaling, params string[] apis) =>
        this.GenerateSource(GetMetadataSource(legacy), allowMarshaling, Platform.X64, apis);

    private void GenerateSource(string source, bool allowMarshaling, Platform platform, params string[] apis)
    {
        this.compilation = this.starterCompilations["net8.0"].WithOptions(this.starterCompilations["net8.0"].Options.WithPlatform(platform));
        string path = EmitMetadata(source, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling, Public = true }, this.compilation, this.parseOptions);
            foreach (string api in apis)
            {
                Assert.True(this.generator.TryGenerate(api, TestContext.Current.CancellationToken));
            }

            this.CollectGeneratedCode(this.generator);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
