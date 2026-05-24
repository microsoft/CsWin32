// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Tests for <see cref="GeneratorOptions.ExtensionReceiver"/> — wraps the generated host static class members
/// in a C# 14 <c>extension (Receiver) { ... }</c> block and routes constants through a private backing field
/// plus a public forwarder property inside the extension block.
/// </summary>
public class ExtensionReceiverTests : GeneratorTestBase
{
    private const string ReceiverName = "PInvoke";
    private const string HostClassName = "WinFormsPInvokes";
    private const string Win32MetadataAssemblyName = "Windows.Win32";

    public ExtensionReceiverTests(ITestOutputHelper logger)
        : base(logger)
    {
        // C# 14 is required to recognize the `extension (T) { ... }` block syntax that the generator emits.
        // The base test harness re-parses generated source through `this.parseOptions`, so without this bump
        // the emitted ExtensionBlockDeclarationSyntax nodes would be lost during re-parsing.
        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp14);
    }

    /// <summary>
    /// Baseline: when the option is not set, generated output is unchanged — no <c>extension (</c> appears anywhere,
    /// and constants remain as plain public/internal fields directly on the host class.
    /// </summary>
    [Fact]
    public void OptionUnset_NoExtensionBlockAndConstantsRemainAsPublicFields()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { EmitSingleFile = true });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("WM_NULL", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        foreach (SyntaxTree tree in this.compilation.SyntaxTrees)
        {
            string text = tree.GetText(TestContext.Current.CancellationToken).ToString();
            Assert.DoesNotContain("extension (", text);
        }

        // The constant should be a directly-declared field on PInvoke (the default host class name).
        FieldDeclarationSyntax wmNull = Assert.Single(this.FindGeneratedConstant("WM_NULL"));
        Assert.DoesNotContain(wmNull.Modifiers, m => m.IsKind(SyntaxKind.PrivateKeyword));
    }

    /// <summary>
    /// When set, per-module extern members and friendly overloads are wrapped in a single
    /// <c>extension (Receiver)</c> block inside the host static class.
    /// </summary>
    [Fact]
    public void Option_WrapsExternMethodsInExtensionBlock()
    {
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal static class {ReceiverName} {{ }} }}",
            fileName: "ReceiverStub.cs");
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        List<ClassDeclarationSyntax> hostClasses = this.FindGeneratedType(HostClassName).OfType<ClassDeclarationSyntax>().ToList();
        ClassDeclarationSyntax host = Assert.Single(hostClasses, c => c.Members.OfType<ExtensionBlockDeclarationSyntax>().Any());
        ExtensionBlockDeclarationSyntax extBlock = Assert.Single(host.Members.OfType<ExtensionBlockDeclarationSyntax>());

        Assert.Equal($"global::Windows.Win32.{ReceiverName}", ReceiverTypeName(extBlock));

        // The extern method should live inside the extension block, not directly on the host class.
        Assert.DoesNotContain(host.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "GetTickCount");
        Assert.Contains(extBlock.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "GetTickCount");
    }

    /// <summary>
    /// Constants must remain as <c>private</c> fields on the host class (C# 14 extension blocks do not allow fields)
    /// while a public/internal forwarder property is generated inside the extension block.
    /// </summary>
    [Fact]
    public void Option_ConstantsAreForwardedViaExtensionProperty()
    {
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal static class {ReceiverName} {{ }} }}",
            fileName: "ReceiverStub.cs");
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("WM_NULL", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        ClassDeclarationSyntax constantHost = SingleHostClass(this.FindGeneratedType(HostClassName));

        // The backing field must still be present on the host class with its original visibility preserved
        // (NOT privatized): this preserves the use of `<HostClass>.<Name>` in C# constant contexts such as
        // enum initializers, attribute arguments, and `fixed`-array sizes. The forwarder property added in
        // the extension block additionally surfaces it through the receiver type for runtime contexts.
        FieldDeclarationSyntax backingField = Assert.Single(
            constantHost.Members.OfType<FieldDeclarationSyntax>(),
            f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "WM_NULL"));
        Assert.DoesNotContain(backingField.Modifiers, m => m.IsKind(SyntaxKind.PrivateKeyword));
        Assert.Contains(backingField.Modifiers, m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword));

        // A forwarder property of the same name must live inside the extension block.
        ExtensionBlockDeclarationSyntax extBlock = Assert.Single(constantHost.Members.OfType<ExtensionBlockDeclarationSyntax>());
        PropertyDeclarationSyntax property = Assert.Single(
            extBlock.Members.OfType<PropertyDeclarationSyntax>(),
            p => p.Identifier.ValueText == "WM_NULL");
        Assert.Contains(property.Modifiers, m => m.IsKind(SyntaxKind.StaticKeyword));

        // The body should forward to the host class's backing field: => WinFormsPInvokes.WM_NULL
        Assert.NotNull(property.ExpressionBody);
        string bodyText = property.ExpressionBody!.Expression.ToString();
        Assert.Contains(HostClassName, bodyText);
        Assert.Contains("WM_NULL", bodyText);
    }

    /// <summary>
    /// In multi-file mode every non-empty partial of the host class carries its own extension block.
    /// </summary>
    [Fact]
    public void Option_MultiFile_EveryPartialWraps()
    {
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal static class {ReceiverName} {{ }} }}",
            fileName: "ReceiverStub.cs");
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = false,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });

        // Two methods from different modules (kernel32 + advapi32) to force multiple partials.
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("RegOpenKeyExW", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        List<ClassDeclarationSyntax> partials = this.FindGeneratedType(HostClassName).OfType<ClassDeclarationSyntax>()
            .Where(c => c.Members.OfType<ExtensionBlockDeclarationSyntax>().Any())
            .ToList();
        Assert.True(partials.Count >= 2, $"Expected at least 2 partials with extension blocks; saw {partials.Count}.");
        foreach (ClassDeclarationSyntax partial in partials)
        {
            Assert.Single(partial.Members.OfType<ExtensionBlockDeclarationSyntax>());
        }
    }

    /// <summary>
    /// End-to-end: generated output compiles cleanly under C# 14 with the receiver type stubbed in the same namespace.
    /// </summary>
    [Fact]
    public void Option_GeneratedCodeCompilesUnderCSharp14()
    {
        // Switch the compilation to .NET 10 (so its references support modern APIs) and parse options to C# 14.
        this.compilation = this.starterCompilations["net10.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp14);

        // The receiver must already exist in the consuming compilation under the same namespace.
        // Non-partial since `FindTypeSymbolsIfAlreadyAvailable` excludes same-assembly partial declarations.
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal static class {ReceiverName} {{ }} }}",
            fileName: "ReceiverStub.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("WM_NULL", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void TryResolveExtensionReceiver_NoOption_ReturnsFalseWithNoError()
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions);
        Generator inner = Assert.IsType<Generator>(((SuperGenerator)this.generator).Generators[Win32MetadataAssemblyName]);
        Assert.False(inner.TryResolveExtensionReceiver(out INamedTypeSymbol? symbol, out string? reason));
        Assert.Null(symbol);
        Assert.Null(reason);
    }

    [Fact]
    public void TryResolveExtensionReceiver_SelfReference_Fails()
    {
        // ExtensionReceiver equals ClassName.
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            ClassName = ReceiverName,
            ExtensionReceiver = ReceiverName,
            EmitSingleFile = true,
        });
        Generator inner = Assert.IsType<Generator>(((SuperGenerator)this.generator).Generators[Win32MetadataAssemblyName]);
        Assert.False(inner.TryResolveExtensionReceiver(out INamedTypeSymbol? symbol, out string? reason));
        Assert.Null(symbol);
        Assert.NotNull(reason);
        Assert.Contains("ClassName", reason);
    }

    [Fact]
    public void TryResolveExtensionReceiver_Unresolved_Fails()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            ClassName = HostClassName,
            ExtensionReceiver = "NonExistentReceiverType",
            EmitSingleFile = true,
        });
        Generator inner = Assert.IsType<Generator>(((SuperGenerator)this.generator).Generators[Win32MetadataAssemblyName]);
        Assert.False(inner.TryResolveExtensionReceiver(out INamedTypeSymbol? symbol, out string? reason));
        Assert.Null(symbol);
        Assert.NotNull(reason);
        Assert.Contains("not found", reason);
    }

    [Fact]
    public void TryResolveExtensionReceiver_NonStaticType_Fails()
    {
        // Add a non-static class with the receiver name in the target namespace.
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal class {ReceiverName} {{ }} }}",
            fileName: "NonStaticReceiver.cs");
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
            EmitSingleFile = true,
        });
        Generator inner = Assert.IsType<Generator>(((SuperGenerator)this.generator).Generators[Win32MetadataAssemblyName]);
        Assert.False(inner.TryResolveExtensionReceiver(out _, out string? reason));
        Assert.NotNull(reason);
        Assert.Contains("static class", reason);
    }

    [Fact]
    public void TryResolveExtensionReceiver_ValidStaticClass_Succeeds()
    {
        // Use a non-partial stub: FindTypeSymbolsIfAlreadyAvailable explicitly excludes same-assembly partial types
        // (in real consumers the receiver lives in a referenced assembly, never partial in the consumer's own).
        this.compilation = this.AddCode(
            $"namespace Windows.Win32 {{ internal static class {ReceiverName} {{ }} }}",
            fileName: "ReceiverStub.cs");
        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
            EmitSingleFile = true,
        });
        Generator inner = Assert.IsType<Generator>(((SuperGenerator)this.generator).Generators[Win32MetadataAssemblyName]);
        Assert.True(inner.TryResolveExtensionReceiver(out INamedTypeSymbol? symbol, out string? reason));
        Assert.NotNull(symbol);
        Assert.Null(reason);
        Assert.Equal(ReceiverName, symbol.Name);
        Assert.True(symbol.IsStatic);
    }

    /// <summary>
    /// Multi-assembly composition: when the receiver type already has an extension method of the same name
    /// (typically declared by another CsWin32-emitted host class in a referenced assembly), the generator
    /// must skip regeneration so the consuming compilation sees only one definition.
    /// </summary>
    [Fact]
    public void Dedup_ExternMethodAlreadyOnReceiver_NotRegenerated()
    {
        // Simulate a "lower" CsWin32 layer that already published GetTickCount as an extension member on PInvoke.
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32
{{
    internal static class {ReceiverName} {{ }}
    internal static class CoreLayerPInvokes
    {{
        extension ({ReceiverName})
        {{
            public static uint GetTickCount() => 0;
        }}
    }}
}}",
            fileName: "ExistingLayer.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // The generator should NOT have regenerated GetTickCount under the new host class.
        IEnumerable<MethodDeclarationSyntax> hostMethods = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "GetTickCount");
        Assert.Empty(hostMethods);
    }

    /// <summary>
    /// Multi-assembly composition: a constant already exposed as an extension member on the receiver is not
    /// re-emitted (neither the private backing field nor the forwarder property).
    /// </summary>
    [Fact]
    public void Dedup_ConstantAlreadyOnReceiver_NotRegenerated()
    {
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32
{{
    internal static class {ReceiverName} {{ }}
    internal static class CoreLayerConstants
    {{
        extension ({ReceiverName})
        {{
            public static uint WM_NULL => 0u;
        }}
    }}
}}",
            fileName: "ExistingConstants.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("WM_NULL", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // Neither the private backing field nor the extension forwarder property should be emitted under HostClassName.
        IEnumerable<FieldDeclarationSyntax> hostFields = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<FieldDeclarationSyntax>())
            .Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "WM_NULL"));
        Assert.Empty(hostFields);

        IEnumerable<PropertyDeclarationSyntax> hostProperties = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            .Where(p => p.Identifier.ValueText == "WM_NULL");
        Assert.Empty(hostProperties);
    }

    /// <summary>
    /// A different name (not pre-existing on the receiver) is still emitted normally.
    /// </summary>
    [Fact]
    public void Dedup_DifferentName_StillEmitted()
    {
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32
{{
    internal static class {ReceiverName} {{ }}
    internal static class CoreLayerPInvokes
    {{
        extension ({ReceiverName})
        {{
            public static uint SomeUnrelated() => 0;
        }}
    }}
}}",
            fileName: "ExistingLayer.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        IEnumerable<MethodDeclarationSyntax> hostMethods = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "GetTickCount");
        Assert.NotEmpty(hostMethods);
    }

    /// <summary>
    /// Regression for WinForms-vet §1.1: name-only dedup is unsafe. A user-authored helper wrapper with
    /// the same simple name but a different parameter count must NOT cause the raw P/Invoke to be skipped,
    /// otherwise the helper silently recurses into itself at runtime. The dedup must match name + arity.
    /// </summary>
    [Fact]
    public void Dedup_SameNameDifferentArity_StillEmitsExtern()
    {
        // The user pre-declares a 2-arg helper on the receiver. The metadata GetTickCount() has 0 args.
        // The previous name-only dedup would skip the extern; the arity-aware dedup must NOT.
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32
{{
    internal static class {ReceiverName} {{ }}
    internal static class CoreLayerPInvokes
    {{
        extension ({ReceiverName})
        {{
            // Same simple name as the raw P/Invoke but different arity (2 args vs 0).
            public static uint GetTickCount(int unused1, int unused2) => 0u;
        }}
    }}
}}",
            fileName: "ExistingLayer.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("GetTickCount", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // The raw 0-arg extern must still be emitted (inside the extension block).
        IEnumerable<MethodDeclarationSyntax> hostMethods = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "GetTickCount" && m.ParameterList.Parameters.Count == 0);
        Assert.NotEmpty(hostMethods);
    }

    /// <summary>
    /// Full signature dedup (name + arity + per-parameter type names): a user-authored helper that matches
    /// the metadata signature's name and parameter count but with a different parameter type must NOT
    /// be treated as a duplicate. Both signatures are valid overloads and both should be emitted.
    /// </summary>
    [Fact]
    public void Dedup_SameNameSameArityDifferentParamType_StillEmitsExtern()
    {
        // IsWindow(HWND) is a 1-arg metadata signature. The user stubs a same-arity helper but with
        // a wholly different parameter type (`int`). Full-signature dedup must not match these.
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32
{{
    internal static class {ReceiverName} {{ }}
    internal static class CoreLayerPInvokes
    {{
        extension ({ReceiverName})
        {{
            // Same name + same arity as the raw P/Invoke (1 param) but the param type differs.
            public static bool IsWindow(int unused) => false;
        }}
    }}
}}",
            fileName: "ExistingLayer.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("IsWindow", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // The raw IsWindow(HWND) must still be emitted because the user's same-arity helper has a
        // different parameter type signature (it's a distinct overload, not a dedup).
        IEnumerable<MethodDeclarationSyntax> hostMethods = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "IsWindow");
        Assert.NotEmpty(hostMethods);
    }

    /// <summary>
    /// Regression for the WinForms-vet second-round case: a user-authored *generic* helper wrapper of the
    /// same simple name with a type-parameter argument must not be treated as a dedup of the raw P/Invoke
    /// even though the wrapper has the same arity as the metadata signature. Specifically mirrors the
    /// WinForms <c>DrawMenuBar&lt;T&gt;(T hWnd) where T : IHandle&lt;HWND&gt;</c> idiom that previously
    /// caused silent self-recursion at runtime.
    /// </summary>
    [Fact]
    public void Dedup_SameNameGenericUserWrapper_StillEmitsExtern()
    {
        // Mirror the WinForms idiom exactly: a generic helper with a constraint that includes the
        // CsWin32-emitted struct (here HWND, which the test stubs as a struct in the same namespace
        // so the constraint binds). The user's `extension(PInvoke) { DrawMenuBar<T>(T hWnd) where T :
        // IHandle<HWND> }` should NOT cause CsWin32 to skip emitting the raw 1-arg `DrawMenuBar(HWND)`.
        this.compilation = this.AddCode(
            $@"namespace Windows.Win32.Foundation
{{
    internal interface IHandle<T> {{ T Handle {{ get; }} }}
    internal readonly struct HWND : IHandle<HWND>
    {{
        public HWND Handle => this;
    }}
}}
namespace Windows.Win32
{{
    using global::Windows.Win32.Foundation;
    internal static class {ReceiverName} {{ }}
    internal static class PrimitivesExtensions
    {{
        extension ({ReceiverName})
        {{
            // Same simple name + same arity as the metadata DrawMenuBar(HWND) but param is a type
            // parameter T. Full-signature comparison must report ""T"" != ""HWND"".
            public static bool DrawMenuBar<T>(T hWnd) where T : IHandle<HWND> => false;
        }}
    }}
}}",
            fileName: "ExistingPrimitives.cs");

        this.generator = this.CreateGenerator(new GeneratorOptions
        {
            EmitSingleFile = true,
            ClassName = HostClassName,
            ExtensionReceiver = ReceiverName,
        });
        Assert.True(this.generator.TryGenerate("DrawMenuBar", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // The raw DrawMenuBar(HWND) (non-generic, single HWND parameter) must still be emitted under
        // the host class because the user's wrapper has a generic type-parameter signature.
        IEnumerable<MethodDeclarationSyntax> hostMethods = this.FindGeneratedType(HostClassName)
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(c => c.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "DrawMenuBar" && m.TypeParameterList is null);
        Assert.NotEmpty(hostMethods);
    }

    private static ClassDeclarationSyntax SingleHostClass(IEnumerable<BaseTypeDeclarationSyntax> types)
    {
        // A single emitted host class with members (the one that actually has the wrap).
        return types.OfType<ClassDeclarationSyntax>().Single(c => c.Members.Count > 0);
    }

    private static string ReceiverTypeName(ExtensionBlockDeclarationSyntax block)
    {
        ParameterSyntax receiverParam = Assert.Single(block.ParameterList!.Parameters);
        Assert.NotNull(receiverParam.Type);
        return receiverParam.Type!.ToString().Trim();
    }
}
