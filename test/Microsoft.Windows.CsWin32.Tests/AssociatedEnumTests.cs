// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Verifies ABI-compatible enum associations and warnings for incompatible metadata.
/// </summary>
public class AssociatedEnumTests : GeneratorTestBase
{
    /// <summary>
    /// Metadata shared by the core and command-line projection tests.
    /// </summary>
    public const string MetadataSource = """
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
                public static extern void Unannotated(uint mode);
            }
        }
        """;

    private const string Caller = """
        using System;
        using Windows.Win32;
        using Windows.Win32.Test;
        static unsafe class Caller
        {
            static void Use(MODE* modes, MODE** indirect, RECORD record)
            {
                MODE flags = MODE.First | MODE.Second | MODE.High;
                MODE result = PInvoke.RoundTrip(flags);
                record.Mode = flags;
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
        namespace Windows.Win32.Test
        {
            interface IModeProperty { MODE Mode { get; set; } }
            public partial struct RECORD : IModeProperty { }
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
    /// Creates fixture declarations with matching native and enum storage.
    /// </summary>
    /// <param name="storage">The integral storage type.</param>
    /// <returns>The metadata declarations.</returns>
    public static string GetMetadataSource(string storage)
    {
        string systemType = storage switch
        {
            "sbyte" => "SByte", "byte" => "Byte", "short" => "Int16", "ushort" => "UInt16",
            "int" => "Int32", "uint" => "UInt32", "long" => "Int64", "ulong" => "UInt64",
            _ => throw new ArgumentException("Unsupported storage.", nameof(storage)),
        };
        return MetadataSource.Replace("using MODE_STORAGE = System.UInt32;", $"using MODE_STORAGE = System.{systemType};", StringComparison.Ordinal)
            .Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", $"enum MODE : {storage} {{ First = 1, Second = 2, High = 4 }}", StringComparison.Ordinal)
            .Replace("fixed uint Modes", $"fixed {storage} Modes", StringComparison.Ordinal);
    }

    /// <summary>
    /// Emits a metadata fixture shared with the command-line generator tests.
    /// </summary>
    /// <param name="source">The fixture declarations.</param>
    /// <param name="references">The reference compilation.</param>
    /// <param name="parseOptions">The language options.</param>
    /// <param name="assemblyName">The metadata assembly identity.</param>
    /// <returns>The temporary metadata path, owned by the caller.</returns>
    public static string EmitMetadata(string source, CSharpCompilation references, CSharpParseOptions parseOptions, string assemblyName = "Windows.Win32.winmd")
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

    /// <summary>
    /// Compiles scalar, pointer, span, output and callback callers in both marshaling modes.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="setLastError">Whether native error preservation needs a wrapper.</param>
    [Theory, CombinatorialData]
    public void MatchingStorageProjectsEnums(bool allowMarshaling, bool setLastError)
    {
        string source = setLastError ? MetadataSource.Replace("ExactSpelling = true", "ExactSpelling = true, SetLastError = true", StringComparison.Ordinal) : MetadataSource;
        this.GenerateSource(source, allowMarshaling, Platform.X64, "RECORD", "RoundTrip", "PointerReturn", "Buffer", "Output", "DoublePointer", "UseCallback", "Unannotated");
        this.compilation = this.AddCode(Caller);
        string callback = allowMarshaling
            ? "static void Register() => PInvoke.UseCallback(Callback); static MODE Callback(MODE mode, MODE* data) => mode;"
            : "static void Register() => PInvoke.UseCallback(&Callback); [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] static MODE Callback(MODE mode, MODE* data) => mode;";
        this.compilation = this.AddCode("using System.Runtime.InteropServices; using Windows.Win32; using Windows.Win32.Test; static unsafe class CallbackCaller { " + callback + " }");
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(((Generator)this.generator!).Diagnostics);
        INamedTypeSymbol record = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.RECORD")!;
        Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(record.GetMembers("Mode")));
        Assert.Equal(SpecialType.System_UInt32, Assert.Single(record.GetMembers("_Mode").OfType<IFieldSymbol>()).Type.SpecialType);
        Assert.Equal(SpecialType.System_UInt32, this.compilation.GetTypeByMetadataName("Windows.Win32.Test.MODE")!.EnumUnderlyingType!.SpecialType);
    }

    /// <summary>
    /// Preserves an annotation on a signature already typed as the same enum.
    /// </summary>
    [Fact]
    public void NativeEnumSignatureIsUnchanged()
    {
        string source = MetadataSource.Replace("MODE_STORAGE RoundTrip([AssociatedEnum(\"MODE\")] MODE_STORAGE mode)", "MODE RoundTrip([AssociatedEnum(\"MODE\")] MODE mode)", StringComparison.Ordinal);
        this.GenerateSource(source, false, Platform.X64, "RoundTrip");
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(((Generator)this.generator!).Diagnostics);
        IMethodSymbol method = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>());
        Assert.Equal(TypeKind.Enum, method.ReturnType.TypeKind);
        Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[0].Type));
    }

    /// <summary>
    /// Preserves all eight enum storage types, independent of CPU, marshaling and request order.
    /// </summary>
    /// <param name="storage">The matching native and enum storage type.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="enumFirst">Whether the enum is requested before its consumers.</param>
    /// <param name="platform">The target CPU.</param>
    [Theory, CombinatorialData]
    public void ExactIntegralStorage(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string storage,
        bool allowMarshaling,
        bool enumFirst,
        [CombinatorialValues(Platform.X86, Platform.X64, Platform.Arm64)] Platform platform)
    {
        string source = GetMetadataSource(storage);
        string[] requests = ["RECORD", "RoundTrip", "PointerReturn", "Output", "DoublePointer", "Buffer", "Unannotated", "FIXED_RECORD"];
        this.GenerateSource(source, allowMarshaling, platform, enumFirst ? ["MODE", .. requests] : requests);
        this.compilation = this.AddCode(Caller);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(((Generator)this.generator!).Diagnostics);
        IMethodSymbol method = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>());
        Assert.Equal(storage, ((INamedTypeSymbol)method.Parameters[0].Type).EnumUnderlyingType!.ToDisplayString());
        Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[0].Type));
    }

    /// <summary>
    /// Warns once per annotation and preserves the complete native shape when enum storage differs.
    /// </summary>
    /// <param name="storage">The incompatible enum storage.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="enumFirst">Whether the enum is independently requested first.</param>
    [Theory, CombinatorialData]
    public void IncompatibleStoragePreservesNativeTypes(
        [CombinatorialValues("ushort", "int", "ulong")] string storage,
        bool allowMarshaling,
        bool enumFirst)
    {
        string source = MetadataSource.Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", $"enum MODE : {storage} {{ First = 1, Second = 2, High = 4 }}", StringComparison.Ordinal);
        string[] requests = ["RECORD", "FIXED_RECORD", "RoundTrip", "PointerReturn", "Buffer", "Output", "DoublePointer", "UseCallback"];
        this.GenerateSource(source, allowMarshaling, Platform.X64, enumFirst ? ["MODE", .. requests] : requests);
        this.compilation = this.AddCode("""
            using System;
            using Windows.Win32;
            using Windows.Win32.Test;
            static unsafe class RawCaller
            {
                static void Use(uint* modes, uint** indirect, RECORD record)
                {
                    uint result = PInvoke.RoundTrip(42);
                    ref uint field = ref record.Mode;
                    uint* fields = record.Modes;
                    PInvoke.Buffer(modes, 2);
                    PInvoke.Buffer(new ReadOnlySpan<uint>(modes, 2));
                    PInvoke.Output(modes);
                    PInvoke.Output(out uint output);
                    PInvoke.DoublePointer(indirect);
                    uint* returned = PInvoke.PointerReturn();
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        IReadOnlyList<Diagnostic> diagnostics = ((Generator)this.generator!).Diagnostics;
        Assert.Equal(12, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("PInvoke015", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("native storage is UInt32", diagnostic.GetMessage(), StringComparison.Ordinal);
        });
        INamedTypeSymbol? mode = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.MODE");
        if (enumFirst)
        {
            Assert.Equal(storage, mode!.EnumUnderlyingType!.ToDisplayString());
        }
        else
        {
            Assert.Null(mode);
        }
    }

    /// <summary>
    /// Executes fixed-buffer access and verifies that scalar fields remain native-backed properties.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory, CombinatorialData]
    public void FieldLayoutAndPropertyContract(bool allowMarshaling)
    {
        this.GenerateSource(MetadataSource, allowMarshaling, Platform.X64, "RECORD", "FIXED_RECORD");
        this.compilation = this.AddCode("""
            using Windows.Win32.Test;
            /// <summary>Exercises the projected field layout.</summary>
            public static unsafe class Layout
            {
                /// <summary>Returns the fixed-buffer size after checking both representations.</summary>
                public static int Run()
                {
                    RECORD scalar = default;
                    scalar.Mode = MODE.High | MODE.Second;
                    if (*(uint*)&scalar != (uint)(MODE.High | MODE.Second)) throw new System.Exception();
                    FIXED_RECORD buffer = default;
                    buffer.Modes.AsSpan()[0] = MODE.High | MODE.First;
                    if (*(uint*)&buffer != (uint)(MODE.High | MODE.First)) throw new System.Exception();
                    return sizeof(FIXED_RECORD);
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        using MemoryStream stream = new();
        var result = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(8, Assembly.Load(stream.ToArray()).GetType("Layout")!.GetMethod("Run")!.Invoke(null, null));
    }

    /// <summary>
    /// Rejects invalid enum targets rather than generating unresolved or ambiguous C# types.
    /// </summary>
    /// <param name="name">The invalid association name.</param>
    /// <param name="message">The expected error detail.</param>
    [Theory]
    [InlineData("Missing", "was not found")]
    [InlineData("RECORD", "does not refer to an enum")]
    [InlineData("", "nonempty enum name")]
    public void InvalidAssociationsFailExplicitly(string name, string message)
    {
        string source = MetadataSource.Replace("AssociatedEnum(\"MODE\")", $"AssociatedEnum(\"{name}\")", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, "RoundTrip"));
        Assert.Contains(message, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Warns for nonintegral native values without changing their representation.
    /// </summary>
    [Fact]
    public void NonintegralNativeValueUsesNativeStorage()
    {
        string source = MetadataSource.Replace("public static extern MODE_STORAGE RoundTrip", "public static extern float RoundTrip", StringComparison.Ordinal);
        this.GenerateSource(source, false, Platform.X64, "RoundTrip");
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Diagnostic warning = Assert.Single(((Generator)this.generator!).Diagnostics);
        Assert.Contains("native storage is Single", warning.GetMessage(), StringComparison.Ordinal);
        IMethodSymbol method = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>());
        Assert.Equal(SpecialType.System_Single, method.ReturnType.SpecialType);
    }

    /// <summary>
    /// Keeps bitfield backing storage primitive instead of applying an enum association to its arithmetic.
    /// </summary>
    [Fact]
    public void BitfieldAssociationUsesNativeStorage()
    {
        string source = MetadataSource.Replace(
            "public sealed class NativeArrayInfoAttribute",
            "public sealed class NativeBitfieldAttribute : Attribute { public NativeBitfieldAttribute(string name, long offset, long length) { } } public sealed class NativeArrayInfoAttribute",
            StringComparison.Ordinal)
            .Replace("[AssociatedEnum(\"MODE\")] public MODE_STORAGE Mode;", "[AssociatedEnum(\"MODE\"), NativeBitfield(\"FirstBit\", 0, 1)] public MODE_STORAGE Mode;", StringComparison.Ordinal);
        this.GenerateSource(source, false, Platform.X64, "RECORD");
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Contains("NativeBitfield", Assert.Single(((Generator)this.generator!).Diagnostics).GetMessage(), StringComparison.Ordinal);
        INamedTypeSymbol record = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.RECORD")!;
        Assert.Equal(SpecialType.System_UInt32, Assert.Single(record.GetMembers("Mode").OfType<IFieldSymbol>()).Type.SpecialType);
        Assert.Single(record.GetMembers("FirstBit").OfType<IPropertySymbol>());
    }

    /// <summary>
    /// Executes direct enum value, return and pointer parameters through a native COM vtable.
    /// </summary>
    [Fact]
    public void UnmanagedComCallPreservesEnumBits()
    {
        const string InterfaceSource = """
            namespace Windows.Win32.Foundation
            {
                public struct HRESULT { public int Value; }
            }
            namespace Windows.Win32.Test
            {
                [ComImport, Guid("E783A761-BE81-4FD7-83F7-80DB453FB7EA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public unsafe interface IScalar
                {
                    [PreserveSig]
                    [return: AssociatedEnum("MODE")]
                    uint Convert([AssociatedEnum("MODE")] uint mode, [AssociatedEnum("MODE")] uint* output);
                }
            }
            """;
        this.GenerateSource(MetadataSource + InterfaceSource, false, Platform.X64, "IScalar");
        this.compilation = this.AddCode("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using Windows.Win32.Test;
            /// <summary>Executes an enum-typed native call.</summary>
            public static unsafe class NativeCaller
            {
                /// <summary>Returns the native return value and output bits.</summary>
                public static ulong Run()
                {
                    void** vtable = stackalloc void*[4];
                    for (int i = 0; i < 4; i++) vtable[i] = (void*)(delegate* unmanaged[Stdcall]<IScalar*, uint, uint*, uint>)&Convert;
                    IScalar instance = default;
                    *(void***)&instance = vtable;
                    MODE output;
                    MODE result = instance.Convert(MODE.High, &output);
                    return ((ulong)(uint)result << 32) | (uint)output;
                }
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                private static uint Convert(IScalar* instance, uint mode, uint* output)
                {
                    *output = mode ^ 1;
                    return mode ^ 2;
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(((Generator)this.generator!).Diagnostics);
        using MemoryStream stream = new();
        var result = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(0x8000000280000001UL, Assembly.Load(stream.ToArray()).GetType("NativeCaller")!.GetMethod("Run")!.Invoke(null, null));
    }

    /// <summary>
    /// Prefers the declaring namespace and diagnoses ambiguous unqualified names.
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
    /// Resolves qualified names through actual assembly references, not arbitrary loaded metadata.
    /// </summary>
    /// <param name="referenced">Whether the native metadata references the enum's assembly.</param>
    [Theory, CombinatorialData]
    public void QualifiedEnumReferenceClosure(bool referenced)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string external = EmitMetadata("namespace Windows.Win32.External { public enum EXTERNAL_MODE : uint { First = 1 } }", this.compilation, this.parseOptions, "Windows.Win32.External.winmd");
        string source = MetadataSource.Replace("AssociatedEnum(\"MODE\")", "AssociatedEnum(\"Windows.Win32.External.EXTERNAL_MODE\")", StringComparison.Ordinal);
        CSharpCompilation references = this.compilation;
        if (referenced)
        {
            source += "namespace Windows.Win32.Test { public struct ANCHOR { public const Windows.Win32.External.EXTERNAL_MODE Value = Windows.Win32.External.EXTERNAL_MODE.First; } }";
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
                Assert.Empty(((SuperGenerator)this.generator).Diagnostics);
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
    /// Warns and keeps native storage if an already available C# enum has a different underlying type.
    /// </summary>
    [Fact]
    public void ReferencedCSharpEnumMustMatchStorage()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        this.compilation = this.AddCode("namespace Windows.Win32.Test { internal enum MODE : ushort { First = 1 } }");
        string path = EmitMetadata(MetadataSource, this.compilation, this.parseOptions);
        try
        {
            Generator generator = new(path, null, [], DefaultTestGeneratorOptions with { Public = true }, this.compilation, this.parseOptions);
            this.generator = generator;
            Assert.True(generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(generator);
            this.compilation = this.AddCode("static class RawCaller { static uint Use() => Windows.Win32.PInvoke.RoundTrip(42); }");
            this.AssertNoDiagnostics(logAllGeneratedCode: false);
            Assert.Equal(2, generator.Diagnostics.Count);
            Assert.All(generator.Diagnostics, diagnostic => Assert.Contains("available C# type", diagnostic.GetMessage(), StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Preserves fixed and flexible array elements and rejects a signedness mismatch without changing layout.
    /// </summary>
    [Fact]
    public void MetadataArrayElements()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "ExternalMetadata", "AssociatedEnumArrays.winmd");
        Generator generator = new(path, null, [], DefaultTestGeneratorOptions with { Public = true, AllowMarshaling = false }, this.compilation, this.parseOptions);
        this.generator = generator;
        Assert.True(generator.TryGenerate("ARRAY_RECORD", TestContext.Current.CancellationToken));
        Assert.True(generator.TryGenerate("ARRAY_FLEX", TestContext.Current.CancellationToken));
        Assert.True(generator.TryGenerate("SIGNED_ARRAY_RECORD", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(generator);
        this.compilation = this.AddCode("""
            using Windows.Win32.Test;
            static unsafe class ArrayCaller
            {
                static void Use(ARRAY_RECORD record, ARRAY_FLEX flexible, SIGNED_ARRAY_RECORD signed)
                {
                    record.Modes.AsSpan()[0] = ARRAY_MODE.First;
                    flexible.Modes.AsSpan(1)[0] = ARRAY_MODE.First;
                    signed.Modes.AsSpan()[0] = -1;
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Diagnostic warning = Assert.Single(generator.Diagnostics);
        Assert.Contains("native storage is Int32", warning.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("underlying type is UInt32", warning.GetMessage(), StringComparison.Ordinal);
        Assert.Null(this.compilation.GetTypeByMetadataName("Windows.Win32.Test.SIGNED_MODE"));
    }

    /// <summary>
    /// Reports the storage warning through the Roslyn source-generator host without failing generation.
    /// </summary>
    [Fact]
    public async Task SourceGeneratorReportsStorageMismatch()
    {
        string source = MetadataSource.Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", "enum MODE : ushort { First = 1 }", StringComparison.Ordinal);
        string path = EmitMetadata(source, this.starterCompilations["net8.0"], this.parseOptions);
        try
        {
            await new CSharpSourceGeneratorVerifier.Test(this.logger)
            {
                ReferenceAssemblies = MyReferenceAssemblies.Net.Net80,
                NativeMethodsTxt = "Buffer",
                GeneratorConfiguration = GeneratorConfiguration.Default with { InputMetadataPaths = [path], InputDocPaths = [] },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(SourceGenerator.IncompatibleAssociatedEnum)
                        .WithArguments("MODE", "Windows.Win32.Test.Apis.Buffer::modes", "native storage is UInt32, but the enum's underlying type is UInt16"),
                },
            }.RunAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
