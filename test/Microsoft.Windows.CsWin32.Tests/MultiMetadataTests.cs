// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;

public class MultiMetadataTests : GeneratorTestBase
{
    public MultiMetadataTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void BasicServiceFabric(bool allowMarshaling)
    {
        this.generator = this.CreateSuperGenerator([.. DefaultMetadataPaths, ServiceFabricMetadataPath], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi("IFabricStringResult");
    }

    [Theory, CombinatorialData]
    public void CrossWinMD_IInspectable(
        [CombinatorialValues([false, true])] bool allowMarshaling,
        [CombinatorialValues([null, "TestPInvoke"])] string pinvokeClassName,
        [CombinatorialValues(["net472", "net8.0"])] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        GeneratorOptions options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        if (pinvokeClassName is not null)
        {
            options = options with { ClassName = pinvokeClassName };
        }

        this.generator = this.CreateSuperGenerator([.. DefaultMetadataPaths, CustomIInspectableMetadataPath], options);
        this.GenerateApi("ITestDerivedFromInspectable");
    }

    [Fact]
    public void CrossWinMD_NTSTATUSSafeHandleConstant()
    {
        this.generator = this.CreateSuperGenerator([WnfWithoutStatusSuccessMetadataPath, MetadataPath], DefaultTestGeneratorOptions);
        this.GenerateApi("RtlSubscribeWnfStateChangeNotification");

        Assert.Contains(this.FindGeneratedType("RtlUnsubscribeWnfStateChangeNotificationSafeHandle"), static type => type is ClassDeclarationSyntax);
        Assert.NotEmpty(this.FindGeneratedConstant("STATUS_SUCCESS"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossWinMD_InterfaceFields(bool allowMarshaling, bool useComSourceGenerators)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string metadataDirectory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(metadataDirectory, "ExternalComConsumer.winmd"), Path.Combine(metadataDirectory, "ExternalComSdk.winmd")],
            DefaultTestGeneratorOptions with
            {
                AllowMarshaling = allowMarshaling,
                ComInterop = new() { UseComSourceGenerators = useComSourceGenerators },
            });

        Assert.True(this.generator.TryGenerate("BINDING", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        StructDeclarationSyntax binding = Assert.Single(this.FindGeneratedType("BINDING").OfType<StructDeclarationSyntax>());
        foreach (string fieldName in new[] { "ExternalCom", "LocalCom" })
        {
            FieldDeclarationSyntax field = this.FindFieldDeclaration(binding, fieldName)!.Value.Field;
            Assert.Equal(!allowMarshaling || useComSourceGenerators, field.Declaration.Type is PointerTypeSyntax);
        }

        foreach (string fieldName in new[] { "ExternalComArray", "LocalComArray" })
        {
            TypeSyntax fieldType = this.FindFieldDeclaration(binding, fieldName)!.Value.Field.Declaration.Type;
            if (allowMarshaling && !useComSourceGenerators)
            {
                Assert.IsType<ArrayTypeSyntax>(fieldType);
            }
            else
            {
                Assert.IsType<PointerTypeSyntax>(Assert.IsType<PointerTypeSyntax>(fieldType).ElementType);
            }
        }

        string comType = useComSourceGenerators ? "IExternalCom_unmanaged*" : allowMarshaling ? "IExternalCom" : "IExternalCom*";
        this.compilation = this.AddCode(
            $$"""
            internal static unsafe class Consumer
            {
                internal static ExternalCom.BINDING Bind(ExternalCom.Sdk.{{comType}} resource) =>
                    new ExternalCom.BINDING { ExternalCom = resource };
            }
            """);
        if (useComSourceGenerators)
        {
            this.RunComSourceGenerators();
        }

        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossWinMD_InterfaceWithNonComAncestry(bool allowMarshaling, bool useComSourceGenerators)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string metadataDirectory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(metadataDirectory, "ExternalComConsumer.winmd"), Path.Combine(metadataDirectory, "ExternalComSdk.winmd")],
            DefaultTestGeneratorOptions with
            {
                AllowMarshaling = allowMarshaling,
                ComInterop = new() { UseComSourceGenerators = useComSourceGenerators },
            });

        Assert.True(this.generator.TryGenerate("NATIVE_BINDING", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        StructDeclarationSyntax binding = Assert.Single(this.FindGeneratedType("NATIVE_BINDING").OfType<StructDeclarationSyntax>());
        foreach (string fieldName in new[] { "ExternalNative", "LocalNative" })
        {
            Assert.IsType<PointerTypeSyntax>(this.FindFieldDeclaration(binding, fieldName)!.Value.Field.Declaration.Type);
        }

        this.compilation = this.AddCode(
            """
            internal static unsafe class Consumer
            {
                internal static ExternalCom.NATIVE_BINDING Bind(ExternalCom.Sdk.IExternalNative* native) =>
                    new ExternalCom.NATIVE_BINDING { ExternalNative = native };
            }
            """);
        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LaterMetadataReplacesTypesAndApiMembers(bool allowMarshaling, bool useComSourceGenerators)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string directory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        SuperGenerator super = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(directory, "OcclusionBaseline.winmd"), Path.Combine(directory, "OcclusionSdk.winmd")],
            DefaultTestGeneratorOptions with
            {
                AllowMarshaling = allowMarshaling,
                ComInterop = new() { UseComSourceGenerators = useComSourceGenerators },
            });
        this.generator = super;
        using Stream baselineStream = File.OpenRead(Path.Combine(directory, "OcclusionBaseline.winmd"));
        using PEReader baselinePe = new(baselineStream);
        MetadataReader reader = baselinePe.GetMetadataReader();
        TypeDefinition bindingDefinition = reader.GetTypeDefinition(reader.TypeDefinitions.Single(h => reader.StringComparer.Equals(reader.GetTypeDefinition(h).Name, "BINDING")));
        BlobReader fieldSignature = reader.GetBlobReader(reader.GetFieldDefinition(bindingDefinition.GetFields().First()).Signature);
        Assert.Equal(SignatureKind.Field, fieldSignature.ReadSignatureHeader().Kind);
        Assert.Equal(0x12, fieldSignature.ReadByte());
        Assert.Equal(HandleKind.TypeDefinition, fieldSignature.ReadTypeHandle().Kind);
        Assert.True(super.Generators["OcclusionBaseline"].TryGenerateType("IResource", out _));
        foreach (string api in new[] { "BINDING", "SDK_BINDING", "IResource", "IResource", "IDerived", "OPTIONS", "OldValue", "NewValue", "CreateResource", "UnrelatedApi", "ReplacedConstant", "UnrelatedConstant" })
        {
            Assert.True(super.TryGenerate(api, TestContext.Current.CancellationToken));
        }

        this.CollectGeneratedCode(super);
        EnumDeclarationSyntax options = Assert.Single(this.FindGeneratedType("OPTIONS").OfType<EnumDeclarationSyntax>());
        Assert.Equal(new[] { "OldValue", "NewValue" }, options.Members.Select(m => m.Identifier.ValueText));
        Assert.Single(this.FindGeneratedType("IResource"));
        string comType = useComSourceGenerators ? "IResource_unmanaged*" : allowMarshaling ? "IResource" : "IResource*";
        string apiComType = allowMarshaling ? "IResource" : "IResource*";
        string memberAccess = allowMarshaling && !useComSourceGenerators ? "." : "->";
        this.compilation = this.AddCode(
            $$"""
            internal static unsafe class Consumer
            {
                internal static Occlusion.Binding.BINDING Bind(Occlusion.Graphics.{{comType}} resource) =>
                    new Occlusion.Binding.BINDING
                    {
                        Buffer = resource,
                        Options = Occlusion.Graphics.OPTIONS.NewValue,
                        Nested = new Occlusion.Graphics.OUTER.INNER { Value = long.MaxValue },
                    };
                internal static int Create(Occlusion.Graphics.{{apiComType}} resource) =>
                    Occlusion.PInvoke.CreateResource(resource);
                internal static Occlusion.Graphics.SDK_BINDING BindReference(Occlusion.Graphics.{{comType}} resource) =>
                    new Occlusion.Graphics.SDK_BINDING { Buffer = resource, Options = Occlusion.Graphics.OPTIONS.NewValue };
                internal static void Invoke(Occlusion.Graphics.{{comType}} resource) => resource{{memberAccess}}NewMethod();
                internal const int Replacement = Occlusion.PInvoke.ReplacedConstant;
                internal const int Unrelated = Occlusion.PInvoke.UnrelatedConstant;
                internal static void Other() => Occlusion.PInvoke.UnrelatedApi();
            }
            """);
        Assert.Equal(2, this.compilation.GetTypeByMetadataName("Consumer")!.GetMembers("Replacement").OfType<IFieldSymbol>().Single().ConstantValue);
        Assert.Equal(1, this.compilation.GetTypeByMetadataName("Consumer")!.GetMembers("Unrelated").OfType<IFieldSymbol>().Single().ConstantValue);
        Assert.Contains(super.GetCompilationUnits(TestContext.Current.CancellationToken), unit => unit.Key.StartsWith("OcclusionSdk.", StringComparison.Ordinal) && unit.Value.ToString().Contains("NewMethod", StringComparison.Ordinal));
        Assert.DoesNotContain(super.GetCompilationUnits(TestContext.Current.CancellationToken), unit => unit.Key.StartsWith("OcclusionBaseline.", StringComparison.Ordinal) && unit.Value.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(t => t.Identifier.ValueText == "IResource"));
        if (useComSourceGenerators)
        {
            this.RunComSourceGenerators();
        }

        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaselineTypesRemainWhenSupplementAbsentOrEarlier(bool includeEarlierSupplement)
    {
        string directory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            includeEarlierSupplement
                ? [MetadataPath, Path.Combine(directory, "OcclusionSdk.winmd"), Path.Combine(directory, "OcclusionBaseline.winmd")]
                : [MetadataPath, Path.Combine(directory, "OcclusionBaseline.winmd")],
            DefaultTestGeneratorOptions);
        this.GenerateApi("BINDING");
        EnumDeclarationSyntax options = Assert.Single(this.FindGeneratedType("OPTIONS").OfType<EnumDeclarationSyntax>());
        Assert.Equal("OldValue", Assert.Single(options.Members).Identifier.ValueText);
    }

    [Theory]
    [InlineData(Platform.X86, "int")]
    [InlineData(Platform.X64, "long")]
    [InlineData(Platform.Arm64, "int")]
    [InlineData(Platform.AnyCpu, "int")]
    public void ReplacementRespectsTargetArchitecture(Platform platform, string fieldType)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        string directory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(directory, "OcclusionBaseline.winmd"), Path.Combine(directory, "OcclusionSdk.winmd")],
            DefaultTestGeneratorOptions);
        this.GenerateApi("BINDING");
        Assert.True(this.generator.TryGenerateType("ARCH_TYPE", out _));
        StructDeclarationSyntax declaration = Assert.Single(this.FindGeneratedType("ARCH_TYPE").OfType<StructDeclarationSyntax>());
        Assert.Equal(fieldType, this.FindFieldDeclaration(declaration, "Value")!.Value.Field.Declaration.Type.ToString());
    }

    [Fact]
    public void AllGenerationUsesReplacementDefinitions()
    {
        string directory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        SuperGenerator super = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(directory, "OcclusionBaseline.winmd"), Path.Combine(directory, "OcclusionSdk.winmd")],
            DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.generator = super;
        super.Generators["OcclusionBaseline"].GenerateAll(TestContext.Current.CancellationToken);
        super.Generators["OcclusionSdk"].GenerateAll(TestContext.Current.CancellationToken);
        this.CollectGeneratedCode(super);
        Assert.Single(this.FindGeneratedType("IResource"));
        Assert.Equal(2, Assert.Single(this.FindGeneratedType("OPTIONS").OfType<EnumDeclarationSyntax>()).Members.Count);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Reproduces the OSClient CsWin32 build configuration: a non-Windows.Win32 metadata assembly generated by
    /// a single <see cref="Generator"/> (no <see cref="SuperGenerator"/> / Windows.Win32 generator to delegate
    /// to), with the Windows.Win32 projection provided by a referenced assembly. The special string typedefs
    /// (<c>PCWSTR</c>, etc.) must be referenced under <c>global::Windows.Win32.Foundation</c>, not under this
    /// assembly's own root namespace (<c>SelfContained.Foundation.PCWSTR</c>, i.e. OSClient's
    /// <c>OSClient.Foundation.PCWSTR</c>), which does not exist and fails to compile with CS0234.
    /// </summary>
    /// <param name="api">The API whose <c>PCWSTR*</c> parameter is projected: <c>Foo</c> (no count, projected
    /// as <c>in PCWSTR</c>) or <c>Bar</c> (counted, projected as <c>ReadOnlySpan&lt;string&gt;</c> with
    /// <c>ArrayPool&lt;PCWSTR&gt;</c> marshaling).</param>
    [Theory]
    [InlineData("Foo")]
    [InlineData("Bar")]
    public void SingleGeneratorQualifiesSpecialTypeDefToWin32Namespace(string api)
    {
        string metadataPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "SelfContained.winmd");

        // The Windows.Win32 projection is referenced (as OSClient references the shared Windows.Win32 output)
        // rather than generated, so PCWSTR must be referenced there.
        this.compilation = this.compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "namespace Windows.Win32.Foundation { internal readonly unsafe struct PCWSTR { internal readonly char* Value; internal PCWSTR(char* value) => this.Value = value; public static implicit operator PCWSTR(char* value) => new PCWSTR(value); } }",
            this.parseOptions,
            path: "Win32Stub.cs",
            cancellationToken: TestContext.Current.CancellationToken));

        // A single Generator (not a SuperGenerator) has no Windows.Win32 generator to delegate special-typedef
        // synthesis to; this is the configuration that reproduced the bug.
        this.generator = new Generator(metadataPath, null, [], DefaultTestGeneratorOptions, this.compilation, this.parseOptions);
        Assert.True(this.generator.TryGenerate(api, TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);

        // Every reference to PCWSTR (extern method, friendly overload param, fixed block, and any ArrayPool
        // temporaries) must be qualified to Windows.Win32.Foundation so it resolves; before the fix these were
        // SelfContained.Foundation.PCWSTR and did not compile.
        this.AssertNoDiagnostics();
        foreach (SyntaxTree tree in this.compilation.SyntaxTrees)
        {
            Assert.DoesNotContain("winmdroot.Foundation.PCWSTR", tree.ToString());
        }
    }

    [Fact]
    public void TypeDefConstant_NestsConstantIntoStruct()
    {
        string metadataPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "TypeDefConstant.winmd");

        this.generator = new Generator(metadataPath, null, [], DefaultTestGeneratorOptions, this.compilation, this.parseOptions);
        Assert.True(this.generator.TryGenerate("CUSTOM_HANDLE_VALUE", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // The constant should be nested inside CUSTOM_HANDLE struct, not dumped as a top-level field on the host class.
        StructDeclarationSyntax customHandle = Assert.Single(this.FindGeneratedType("CUSTOM_HANDLE").OfType<StructDeclarationSyntax>());
        Assert.Contains(customHandle.Members.OfType<FieldDeclarationSyntax>(), f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "CUSTOM_HANDLE_VALUE"));

        // No top-level constant on the host class should be generated.
        Assert.Empty(this.FindGeneratedType(DefaultTestGeneratorOptions.ClassName));
    }

    [Fact]
    public void TypeDefConstant_WithExtensionReceiver_AttachesToStructExtensionBlock()
    {
        string metadataPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "TypeDefConstant.winmd");

        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp14);

        // Provide the receiver and the struct externally so extensionReceiver attaches forwarders to the struct.
        this.compilation = this.AddCode(
            @"namespace TypeDefConstant { internal static class PInvoke { } }
namespace TypeDefConstant.Foundation
{
    internal readonly struct CUSTOM_HANDLE
    {
        internal readonly int Value;
        internal CUSTOM_HANDLE(int value) => this.Value = value;
        public static explicit operator CUSTOM_HANDLE(int value) => new CUSTOM_HANDLE(value);
    }
}",
            fileName: "Refs.cs");

        GeneratorOptions options = new()
        {
            EmitSingleFile = true,
            ClassName = "PInvokeExtensions",
            ExtensionReceiver = "PInvoke",
        };
        this.generator = new Generator(metadataPath, null, [], options, this.compilation, this.parseOptions);

        Assert.True(this.generator.TryGenerate("CUSTOM_HANDLE_VALUE", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        ClassDeclarationSyntax hostClass = Assert.Single(
            this.FindGeneratedType("PInvokeExtensions").OfType<ClassDeclarationSyntax>(),
            c => c.Members.Count > 0);

        // Under extensionReceiver when the struct is external, CUSTOM_HANDLE_VALUE attaches to extension(CUSTOM_HANDLE),
        // not to extension(PInvoke).
        ExtensionBlockDeclarationSyntax structExtension = Assert.Single(
            hostClass.Members.OfType<ExtensionBlockDeclarationSyntax>(),
            b => b.ParameterList!.Parameters.Single().Type!.ToString().Trim() == "global::TypeDefConstant.Foundation.CUSTOM_HANDLE");
        Assert.Contains(structExtension.Members.OfType<PropertyDeclarationSyntax>(), p => p.Identifier.ValueText == "CUSTOM_HANDLE_VALUE");
    }

    private void RunComSourceGenerators()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "ComSourceGenerators");
        ISourceGenerator[] generators = new[] { "Microsoft.Interop.ComInterfaceGenerator.dll", "Microsoft.Interop.LibraryImportGenerator.dll" }
            .SelectMany(name => Assembly.LoadFrom(Path.Combine(directory, name)).GetTypes())
            .Where(type => type.GetCustomAttribute<GeneratorAttribute>() is not null)
            .Select(type => ((IIncrementalGenerator)Activator.CreateInstance(type)!).AsSourceGenerator())
            .ToArray();
        Assert.NotEmpty(generators);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: this.parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(this.compilation, out Compilation updatedCompilation, out ImmutableArray<Diagnostic> diagnostics);
        this.LogDiagnostics(diagnostics);
        Assert.Empty(diagnostics);
        ImmutableArray<SyntaxTree> generatedTrees = driver.GetRunResult().GeneratedTrees;
        Assert.NotEmpty(generatedTrees);
        Microsoft.CodeAnalysis.Emit.EmitResult emit = updatedCompilation.Emit(Stream.Null);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        this.compilation = (CSharpCompilation)updatedCompilation;
        foreach (SyntaxTree tree in generatedTrees)
        {
            // The SDK generator mixes LF and CRLF on Linux; CsWin32's line-ending checks apply to our output.
            SyntaxTree normalizedTree = CSharpSyntaxTree.ParseText(TestUtils.NormalizeToExpectedLineEndings(tree.ToString()), this.parseOptions, tree.FilePath);
            this.compilation = this.compilation.ReplaceSyntaxTree(tree, normalizedTree);
        }
    }
}
