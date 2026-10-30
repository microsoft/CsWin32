// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    [InlineData(false)]
    [InlineData(true)]
    public void CrossWinMD_InterfaceFields(bool allowMarshaling)
    {
        string metadataDirectory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(metadataDirectory, "ExternalComConsumer.winmd"), Path.Combine(metadataDirectory, "ExternalComSdk.winmd")],
            DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });

        this.GenerateApi("BINDING");

        StructDeclarationSyntax binding = Assert.Single(this.FindGeneratedType("BINDING").OfType<StructDeclarationSyntax>());
        foreach (string fieldName in new[] { "ExternalCom", "LocalCom" })
        {
            FieldDeclarationSyntax field = this.FindFieldDeclaration(binding, fieldName)!.Value.Field;
            Assert.Equal(!allowMarshaling, field.Declaration.Type is PointerTypeSyntax);
        }

        foreach (string fieldName in new[] { "ExternalComArray", "LocalComArray" })
        {
            TypeSyntax fieldType = this.FindFieldDeclaration(binding, fieldName)!.Value.Field.Declaration.Type;
            if (allowMarshaling)
            {
                Assert.IsType<ArrayTypeSyntax>(fieldType);
            }
            else
            {
                Assert.IsType<PointerTypeSyntax>(Assert.IsType<PointerTypeSyntax>(fieldType).ElementType);
            }
        }

        string comType = allowMarshaling ? "IExternalCom" : "IExternalCom*";
        this.compilation = this.AddCode(
            $$"""
            internal static unsafe class Consumer
            {
                internal static ExternalCom.BINDING Bind(ExternalCom.{{comType}} resource) =>
                    new ExternalCom.BINDING { ExternalCom = resource };
            }
            """);
        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossWinMD_InterfaceWithNonComAncestry(bool allowMarshaling)
    {
        string metadataDirectory = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata");
        this.generator = this.CreateSuperGenerator(
            [MetadataPath, Path.Combine(metadataDirectory, "ExternalComConsumer.winmd"), Path.Combine(metadataDirectory, "ExternalComSdk.winmd")],
            DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });

        this.GenerateApi("NATIVE_BINDING");

        StructDeclarationSyntax binding = Assert.Single(this.FindGeneratedType("NATIVE_BINDING").OfType<StructDeclarationSyntax>());
        foreach (string fieldName in new[] { "ExternalNative", "LocalNative" })
        {
            Assert.IsType<PointerTypeSyntax>(this.FindFieldDeclaration(binding, fieldName)!.Value.Field.Declaration.Type);
        }

        this.compilation = this.AddCode(
            """
            internal static unsafe class Consumer
            {
                internal static ExternalCom.NATIVE_BINDING Bind(ExternalCom.IExternalNative* native) =>
                    new ExternalCom.NATIVE_BINDING { ExternalNative = native };
            }
            """);
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
}
