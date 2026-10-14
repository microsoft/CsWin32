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
            [Flags]
            public enum NARROW_MODE : uint { First = 1, Second = 2, High = 0x8000 }
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
                [AssociatedEnum("NARROW_MODE")] public ushort Mode;
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
                public static extern void Narrow([AssociatedEnum("NARROW_MODE")] ushort mode);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: AssociatedEnum("NARROW_MODE")]
                public static extern ushort NarrowReturn();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void NarrowPointer([AssociatedEnum("NARROW_MODE")] ushort* mode);
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
    /// <param name="storage">The native use site's storage type.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="platform">The target CPU.</param>
    [Theory, CombinatorialData]
    public void ExactIntegralStorage(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string storage,
        bool allowMarshaling,
        [CombinatorialValues(Platform.X86, Platform.X64, Platform.Arm64)] Platform platform)
    {
        string source = GetNativeStorageMetadata(storage, legacy: false);
        this.GenerateSource(source, allowMarshaling, platform, "RECORD", "RoundTrip", "PointerReturn", "Output", "DoublePointer", "Buffer", "Unannotated");
        this.compilation = this.AddCode(Caller);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        IMethodSymbol roundTrip = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("RoundTrip").OfType<IMethodSymbol>());
        Assert.Equal(storage, ((INamedTypeSymbol)roundTrip.ReturnType).EnumUnderlyingType!.ToDisplayString());
    }

    /// <summary>
    /// Verifies inline buffers and direct enum fields whose storage comes from their native use sites.
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
                    checked { narrow.Mode = NARROW_MODE.High | NARROW_MODE.Second; }
                    ref NARROW_MODE mode = ref narrow.Mode;
                    if (record.Modes.AsSpan()[0] != (MODE.First | MODE.High) || mode != (NARROW_MODE.High | NARROW_MODE.Second)) throw new System.Exception();
                    return sizeof(FIXED_RECORD) * 100 + sizeof(NARROW_RECORD);
                }
                static void Compile() => PInvoke.Narrow(NARROW_MODE.Second);
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
    public void InvalidAssociationsFailExplicitly(string replacement, string api, string message)
    {
        string source = MetadataSource.Replace("AssociatedEnum(\"MODE\")", $"AssociatedEnum(\"{replacement}\")", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, api));
        Assert.Contains(message, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects incompatible widths or signedness for one enum independently of generation order.
    /// </summary>
    /// <param name="firstRequest">The first requested enum or consumer.</param>
    [Theory]
    [InlineData("MODE")]
    [InlineData("RoundTrip")]
    [InlineData("RECORD")]
    public void ConflictingNativeStorageFailsDeterministically(string firstRequest)
    {
        const string ConflictMethod = """
            public static extern void Unannotated(uint mode);
            [DllImport("fixture.dll", ExactSpelling = true)]
            public static extern void Conflict([AssociatedEnum("MODE")] int mode);
            """;
        string source = MetadataSource.Replace("public static extern void Unannotated(uint mode);", ConflictMethod, StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, firstRequest));
        Assert.Contains("conflicting native storage", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("UInt32", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("Int32", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("Apis.Conflict::mode", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Does not silently truncate logical members when narrowing the generated enum's native storage.
    /// </summary>
    [Fact]
    public void OutOfRangeMemberFailsExplicitly()
    {
        string source = MetadataSource.Replace("High = 0x8000 }", "High = 0x10000 }", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, "Narrow"));
        Assert.Contains("High=65536 does not fit native UInt16 storage without truncation", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects nonintegral native use sites instead of selecting a logical enum's arbitrary metadata storage.
    /// </summary>
    [Fact]
    public void NonintegralNativeStorageFailsExplicitly()
    {
        string source = MetadataSource.Replace("public static extern MODE_STORAGE RoundTrip", "public static extern float RoundTrip", StringComparison.Ordinal);
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(source, false, Platform.X64, "RoundTrip"));
        Assert.Contains("integral native storage", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Retains real native enum ABI and raw scalar callers while offering explicit enum overloads.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeEnumSignatureRetainsItsStorage(bool allowMarshaling)
    {
        const string NativeEnumMethod = """
            public static extern void Unannotated(uint mode);
            [DllImport("fixture.dll", ExactSpelling = true)]
            public static extern void NativeEnum(NARROW_MODE mode);
            """;
        string source = MetadataSource.Replace("public static extern void Unannotated(uint mode);", NativeEnumMethod, StringComparison.Ordinal);
        this.GenerateSource(source, allowMarshaling, Platform.X64, "NARROW_MODE", "NativeEnum", "Narrow", "NarrowReturn");
        this.AssertNoDiagnostics();
        INamedTypeSymbol mode = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.NARROW_MODE")!;
        Assert.Equal(SpecialType.System_UInt32, mode.EnumUnderlyingType!.SpecialType);
        this.compilation = this.AddCode("using Windows.Win32; using Windows.Win32.Test; static class ExistingScalarCaller { static void Raw(ushort value) => PInvoke.Narrow(value); static void Enum(NARROW_MODE value) => PInvoke.Narrow(value); static ushort RawReturn() => PInvoke.NarrowReturn(); static NARROW_MODE EnumReturn() => PInvoke.NarrowReturn_Enum(); }");
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Rejects reinterpreting an indirect association whose enum storage is fixed by real native enum signatures.
    /// </summary>
    [Fact]
    public void NativeEnumIndirectConflictFailsExplicitly()
    {
        const string NativeEnumField = "namespace Windows.Win32.Test { public struct NATIVE_RECORD { public NARROW_MODE Value; } }";
        GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.GenerateSource(
            MetadataSource + Environment.NewLine + NativeEnumField, false, Platform.X64, "NarrowPointer"));
        Assert.Contains("conflicting native storage", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Preserves the existing scalar property when a real native enum signature fixes different storage.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeEnumFieldRetainsExistingScalarProperty(bool allowMarshaling)
    {
        const string NativeEnumField = "namespace Windows.Win32.Test { public struct NATIVE_RECORD { public NARROW_MODE Value; } }";
        this.GenerateSource(MetadataSource + Environment.NewLine + NativeEnumField, allowMarshaling, Platform.X64, "NARROW_RECORD", "NATIVE_RECORD");
        INamedTypeSymbol mode = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.NARROW_MODE")!;
        Assert.Equal(SpecialType.System_UInt32, mode.EnumUnderlyingType!.SpecialType);
        INamedTypeSymbol record = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.NARROW_RECORD")!;
        Assert.Equal(mode, Assert.Single(record.GetMembers("Mode").OfType<IPropertySymbol>()).Type, SymbolEqualityComparer.Default);
        IFieldSymbol storage = Assert.Single(record.GetMembers("_Mode").OfType<IFieldSymbol>());
        Assert.Equal(SpecialType.System_UInt16, storage.Type.SpecialType);
        Assert.Equal(Accessibility.Private, storage.DeclaredAccessibility);
        this.compilation = this.AddCode("using Windows.Win32.Test; static class ExistingFieldCaller { static ushort Use(NARROW_RECORD record) { record.Mode = NARROW_MODE.First; return (ushort)record.Mode; } }");
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Executes a scalar enum adapter through an unmanaged COM vtable without changing its native signature.
    /// </summary>
    [Fact]
    public void NativeEnumComAdapterPreservesNativeBits()
    {
        const string NativeEnumInterface = """
            namespace Windows.Win32.Foundation
            {
                public struct HRESULT { public int Value; }
            }
            namespace Windows.Win32.Test
            {
                public struct NATIVE_RECORD { public NARROW_MODE Value; }
                [ComImport, Guid("E783A761-BE81-4FD7-83F7-80DB453FB7EA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IScalar
                {
                    [PreserveSig]
                    [return: AssociatedEnum("NARROW_MODE")]
                    ushort Convert([AssociatedEnum("NARROW_MODE")] ushort mode);
                }
            }
            """;
        this.GenerateSource(MetadataSource + Environment.NewLine + NativeEnumInterface, false, Platform.X64, "IScalar");
        this.compilation = this.AddCode("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using Windows.Win32.Test;
            /// <summary>Executes the native scalar adapter.</summary>
            public static unsafe class ComAdapterCaller
            {
                /// <summary>Returns the native result after conversion.</summary>
                public static uint Run()
                {
                    void** vtable = stackalloc void*[4];
                    for (int i = 0; i < 4; i++) vtable[i] = (void*)(delegate* unmanaged[Stdcall]<IScalar*, ushort, ushort>)&Convert;
                    IScalar instance = default;
                    *(void***)&instance = vtable;
                    return (uint)instance.Convert((NARROW_MODE)0x18001);
                }
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                private static ushort Convert(IScalar* instance, ushort mode) => (ushort)(mode ^ 2);
            }
            """);
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithOverflowChecks(true));
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        using MemoryStream stream = new();
        var result = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(0x8003U, Assembly.Load(stream.ToArray()).GetType("ComAdapterCaller")!.GetMethod("Run")!.Invoke(null, null));
    }

    /// <summary>
    /// Keeps source-generated imports native while the scalar enum view is an ordinary managed adapter.
    /// </summary>
    [Fact]
    public void NativeEnumAdapterKeepsLibraryImportRaw()
    {
        const string NativeEnumField = "namespace Windows.Win32.Test { public struct NATIVE_RECORD { public NARROW_MODE Value; } }";
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = EmitMetadata(MetadataSource + Environment.NewLine + NativeEnumField, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true, ComInterop = new() { UseComSourceGenerators = true } }, this.compilation, this.parseOptions);
            Assert.True(this.generator.TryGenerate("Narrow", TestContext.Current.CancellationToken));
            Assert.True(this.generator.TryGenerate("NarrowReturn", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(this.generator);
            IMethodSymbol[] methods = this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("Narrow").OfType<IMethodSymbol>().ToArray();
            Assert.Contains(methods, method => method.Parameters[0].Type.SpecialType == SpecialType.System_UInt16 && method.IsPartialDefinition);
            Assert.Contains(methods, method => method.Parameters[0].Type.TypeKind == TypeKind.Enum && !method.IsPartialDefinition);
            Assert.DoesNotContain(this.compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes()).OfType<AttributeSyntax>(), attribute =>
                attribute.Name.ToString().Contains("MarshalUsing", StringComparison.Ordinal));
            this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false, acceptable: diagnostic => diagnostic.Id == "CS8795");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Does not decode unsupported native storage belonging to a different qualified enum with the same leaf name.
    /// </summary>
    [Fact]
    public void UnrelatedQualifiedAssociationDoesNotAffectStorage()
    {
        const string UnrelatedSource = """
            namespace Windows.Win32.Other
            {
                public enum MODE : uint { First = 1 }
                public unsafe struct RECORD
                {
                    [Windows.Win32.Foundation.Metadata.AssociatedEnum("Windows.Win32.Other.MODE")]
                    public fixed float Modes[2];
                }
            }
            """;
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = EmitMetadata(MetadataSource + Environment.NewLine + UnrelatedSource, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true }, this.compilation, this.parseOptions);
            Assert.True(this.generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics();
            INamedTypeSymbol mode = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.MODE")!;
            Assert.Equal(SpecialType.System_UInt32, mode.EnumUnderlyingType!.SpecialType);
            GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.generator.TryGenerate("Windows.Win32.Other.RECORD", TestContext.Current.CancellationToken));
            Assert.Contains("System.Single", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Preserves native high-bit flags when the logical enum metadata uses different signedness.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeSignednessPreservesHighBitFlags(bool allowMarshaling)
    {
        string source = MetadataSource.Replace("using MODE_STORAGE = System.UInt32;", "using MODE_STORAGE = System.Int32;", StringComparison.Ordinal)
            .Replace("fixed uint Modes", "fixed int Modes", StringComparison.Ordinal);
        this.GenerateSource(source, allowMarshaling, Platform.X64, "MODE", "RoundTrip", "RECORD", "PointerReturn", "Output", "DoublePointer", "Buffer", "Unannotated");
        this.compilation = this.AddCode(Caller);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        INamedTypeSymbol mode = this.compilation.GetTypeByMetadataName("Windows.Win32.Test.MODE")!;
        Assert.Equal(SpecialType.System_Int32, mode.EnumUnderlyingType!.SpecialType);
        Assert.Equal(int.MinValue, Assert.Single(mode.GetMembers("High").OfType<IFieldSymbol>()).ConstantValue);
    }

    /// <summary>
    /// Rejects an already available C# enum whose storage disagrees with the native use site.
    /// </summary>
    [Fact]
    public void ReferencedEnumMustMatchNativeStorage()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = EmitMetadata(MetadataSource, this.compilation, this.parseOptions);
        try
        {
            this.compilation = this.AddCode("namespace Windows.Win32.Test { public enum MODE : ushort { First = 1 } }");
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true }, this.compilation, this.parseOptions);
            GenerationFailedException exception = Assert.Throws<GenerationFailedException>(() => this.generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
            Assert.Contains("requires UInt32 storage", exception.ToString(), StringComparison.Ordinal);
            Assert.Contains("referenced C# enum uses ushort", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
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
        string external = EmitMetadata("namespace Windows.Win32.External { public enum EXTERNAL_MODE : long { First = 1 } }", this.compilation, this.parseOptions, "Windows.Win32.External.winmd");
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
                Assert.True(this.generator.TryGenerate("Windows.Win32.External.EXTERNAL_MODE", TestContext.Current.CancellationToken));
                Assert.True(this.generator.TryGenerate("RoundTrip", TestContext.Current.CancellationToken));
                this.CollectGeneratedCode(this.generator);
                this.compilation = this.AddCode("static class ExternalCaller { static void Use() => Windows.Win32.PInvoke.RoundTrip(Windows.Win32.External.EXTERNAL_MODE.First); }");
                this.AssertNoDiagnostics(logAllGeneratedCode: false);
                Assert.Equal(SpecialType.System_UInt32, this.compilation.GetTypeByMetadataName("Windows.Win32.External.EXTERNAL_MODE")!.EnumUnderlyingType!.SpecialType);
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
    /// Verifies fixed-rank arrays and native signed element storage.
    /// </summary>
    [Fact]
    public void MetadataArrayElements()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "AssociatedEnumArrays.winmd");
        this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true, AllowMarshaling = false }, this.compilation, this.parseOptions);
        Assert.True(this.generator.TryGenerate("ARRAY_RECORD", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("ARRAY_FLEX", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("SIGNED_ARRAY_RECORD", TestContext.Current.CancellationToken));
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
                    SIGNED_ARRAY_RECORD signed = default;
                    signed.Modes.AsSpan()[0] = SIGNED_MODE.First;
                    if (sizeof(SIGNED_ARRAY_RECORD) != 8) throw new System.Exception();
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
        Assert.Equal(SpecialType.System_Int32, this.compilation.GetTypeByMetadataName("Windows.Win32.Test.SIGNED_MODE")!.EnumUnderlyingType!.SpecialType);
    }

    /// <summary>
    /// Uses direct native-storage enum signatures, without conversion marshalers, with source generators.
    /// </summary>
    /// <param name="native">The native storage code.</param>
    [Theory, CombinatorialData]
    public void SourceGeneratedIntegralProjection(
        [CombinatorialValues("sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong")] string native)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string source = MetadataSource.Replace("ushort", native, StringComparison.Ordinal)
            .Replace("enum NARROW_MODE : uint { First = 1, Second = 2, High = 0x8000 }", $"enum NARROW_MODE : {(native == "ulong" ? "int" : "ulong")} {{ First = 1, Second = 2, High = 4 }}", StringComparison.Ordinal);
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
            INamedTypeSymbol projected = Assert.IsAssignableFrom<INamedTypeSymbol>(method.Parameters[0].Type);
            Assert.Equal(native, projected.EnumUnderlyingType!.ToDisplayString());
            Assert.DoesNotContain(method.Parameters[0].GetAttributes(), attribute => attribute.AttributeClass?.Name is "MarshalUsingAttribute" or "MarshalAsAttribute");
            IMethodSymbol returning = Assert.Single(pinvoke.GetMembers("NarrowReturn").OfType<IMethodSymbol>());
            Assert.True(SymbolEqualityComparer.Default.Equals(projected, returning.ReturnType));
            Assert.DoesNotContain(returning.GetReturnTypeAttributes(), attribute => attribute.AttributeClass?.Name is "MarshalUsingAttribute" or "MarshalAsAttribute");
            this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false, acceptable: diagnostic => diagnostic.Id == "CS8795");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Projects indirect native enum storage directly, including counted buffers and direction annotations.
    /// </summary>
    /// <param name="attributes">The pointer's direction and array annotations.</param>
    [Theory]
    [InlineData("")]
    [InlineData("In, ")]
    [InlineData("Out, ")]
    [InlineData("In, Out, ")]
    [InlineData("In, NativeArrayInfo(CountConst = 2), ")]
    public void SourceGeneratedIndirectProjection(string attributes)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string source = MetadataSource.Replace("void NarrowPointer([AssociatedEnum", "void NarrowPointer([" + attributes + "AssociatedEnum", StringComparison.Ordinal);
        string path = EmitMetadata(source, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true, ComInterop = new() { UseComSourceGenerators = true } }, this.compilation, this.parseOptions);
            Assert.True(this.generator.TryGenerate("NarrowPointer", TestContext.Current.CancellationToken));
            Assert.True(this.generator.TryGenerate("Buffer", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(this.generator);
            IMethodSymbol buffer = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("Buffer").OfType<IMethodSymbol>(), method => method.Parameters.Length == 2);
            IPointerTypeSymbol pointer = Assert.IsAssignableFrom<IPointerTypeSymbol>(buffer.Parameters[0].Type);
            Assert.Equal("Windows.Win32.Test.MODE", pointer.PointedAtType.ToDisplayString());
            Assert.Equal(SpecialType.System_UInt16, this.compilation.GetTypeByMetadataName("Windows.Win32.Test.NARROW_MODE")!.EnumUnderlyingType!.SpecialType);
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
    /// Creates matching synthetic/native fixtures while deliberately varying the logical enum metadata storage.
    /// </summary>
    /// <param name="storage">The native integral storage.</param>
    /// <param name="legacy">Whether signatures use synthetic enums directly.</param>
    /// <returns>The OLD or NEW metadata source.</returns>
    internal static string GetNativeStorageMetadata(string storage, bool legacy)
    {
        string systemType = storage switch
        {
            "sbyte" => "SByte", "byte" => "Byte", "short" => "Int16", "ushort" => "UInt16",
            "int" => "Int32", "uint" => "UInt32", "long" => "Int64", "ulong" => "UInt64",
            _ => throw new ArgumentException("Unsupported native enum storage.", nameof(storage)),
        };
        string logicalStorage = legacy ? storage : storage == "ulong" ? "int" : "ulong";
        return GetMetadataSource(legacy)
            .Replace("using MODE_STORAGE = System.UInt32;", $"using MODE_STORAGE = System.{systemType};", StringComparison.Ordinal)
            .Replace("enum MODE : uint { First = 1, Second = 2, High = 0x80000000 }", $"enum MODE : {logicalStorage} {{ First = 1, Second = 2, High = 4 }}", StringComparison.Ordinal)
            .Replace("fixed uint Modes", $"fixed {storage} Modes", StringComparison.Ordinal);
    }

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
