// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;

public class AgilitySdkTests : GeneratorTestBase
{
    private static readonly string BaselinePath = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "Windows.SdkBaseline.winmd");
    private static readonly string AgilityPath = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "ExternalMetadata", "Microsoft.Direct3D.D3D12.winmd");

    public AgilitySdkTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PublicAgilityReplacesWindowsTypes(bool allowMarshaling, bool useComSourceGenerators, bool reverseRequests)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateSuperGenerator(
            [BaselinePath, AgilityPath],
            DefaultTestGeneratorOptions with
            {
                AllowMarshaling = allowMarshaling,
                ComInterop = new() { UseComSourceGenerators = useComSourceGenerators },
            });
        string[] requests =
        [
            "CreateDXGIFactory2", "IDXGIFactory4", "IDXGIAdapter", "D3D12CreateDevice",
            "ID3D12Device", "ID3D12Resource", "ID3D12Resource", "D3D12_FEATURE",
            "DML_BUFFER_BINDING", "DMLCreateDevice", "D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING",
            "D3D11_SAMPLER_DESC", "D3D12_SAMPLER_DESC",
        ];
        if (reverseRequests)
        {
            Array.Reverse(requests);
        }

        foreach (string request in requests)
        {
            Assert.True(this.generator.TryGenerate(request, TestContext.Current.CancellationToken));
        }

        this.CollectGeneratedCode(this.generator);
        Assert.Single(this.FindGeneratedType("ID3D12Resource"));
        Assert.Single(this.FindGeneratedType("__float_4"));
        EnumDeclarationSyntax feature = Assert.Single(this.FindGeneratedType("D3D12_FEATURE").OfType<EnumDeclarationSyntax>());
        using Stream sdkStream = File.OpenRead(AgilityPath);
        using PEReader sdkPe = new(sdkStream);
        MetadataReader reader = sdkPe.GetMetadataReader();
        TypeDefinition enumDefinition = reader.GetTypeDefinition(reader.TypeDefinitions.Single(h => reader.StringComparer.Equals(reader.GetTypeDefinition(h).Name, "D3D12_FEATURE")));
        string[] expectedMembers = enumDefinition.GetFields().Select(h => reader.GetFieldDefinition(h))
            .Where(f => !f.GetDefaultValue().IsNil).Select(f => reader.GetString(f.Name)).ToArray();
        Assert.Equal(expectedMembers, feature.Members.Select(m => m.Identifier.ValueText));
        Assert.Contains("D3D12_FEATURE_D3D12_OPTIONS22", expectedMembers);
        Assert.Contains("D3D12_FEATURE_D3D12_OPTIONS", expectedMembers);
        foreach (string name in new[] { "ID3D12Resource", "D3D12_FEATURE" })
        {
            Assert.Contains(this.generator.GetCompilationUnits(TestContext.Current.CancellationToken), unit =>
                unit.Key.StartsWith("Microsoft.Direct3D.D3D12.", StringComparison.Ordinal) &&
                unit.Value.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(t => t.Identifier.ValueText == name));
            Assert.DoesNotContain(this.generator.GetCompilationUnits(TestContext.Current.CancellationToken), unit =>
                unit.Key.StartsWith("Windows.Win32.", StringComparison.Ordinal) &&
                unit.Value.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(t => t.Identifier.ValueText == name));
        }

        string resourceType = useComSourceGenerators ? "ID3D12Resource_unmanaged*" : allowMarshaling ? "ID3D12Resource" : "ID3D12Resource*";
        this.compilation = this.AddCode(
            $$"""
            internal static unsafe class Consumer
            {
                internal const string Factory = nameof(Windows.Win32.PInvoke.D3D12CreateDevice);
                internal const Windows.Win32.Graphics.Direct3D12.D3D12_FEATURE NewFeature =
                    Windows.Win32.Graphics.Direct3D12.D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS22;
                internal static Windows.Win32.AI.MachineLearning.DirectML.DML_BUFFER_BINDING Bind(
                    Windows.Win32.Graphics.Direct3D12.{{resourceType}} resource) =>
                    new Windows.Win32.AI.MachineLearning.DirectML.DML_BUFFER_BINDING { Buffer = resource };
                internal static float Border(in Windows.Win32.Graphics.Direct3D11.D3D11_SAMPLER_DESC sampler) =>
                    Windows.Win32.InlineArrayIndexerExtensions.ReadOnlyItemRef(in sampler.BorderColor, 0);
                internal static float Border(in Windows.Win32.Graphics.Direct3D12.D3D12_SAMPLER_DESC sampler) =>
                    Windows.Win32.InlineArrayIndexerExtensions.ReadOnlyItemRef(in sampler.BorderColor, 0);
            }
            """);
        Assert.Equal(65, this.compilation.GetTypeByMetadataName("Consumer")!.GetMembers("NewFeature").OfType<IFieldSymbol>().Single().ConstantValue);
        INamedTypeSymbol projectedEnum = this.compilation.GetTypeByMetadataName("Windows.Win32.Graphics.Direct3D12.D3D12_FEATURE")!;
        foreach (FieldDefinitionHandle handle in enumDefinition.GetFields())
        {
            FieldDefinition field = reader.GetFieldDefinition(handle);
            if (!field.GetDefaultValue().IsNil)
            {
                BlobReader value = reader.GetBlobReader(reader.GetConstant(field.GetDefaultValue()).Value);
                Assert.Equal(value.ReadInt32(), projectedEnum.GetMembers(reader.GetString(field.Name)).OfType<IFieldSymbol>().Single().ConstantValue);
            }
        }

        ClassDeclarationSyntax[] arrayHelpers = this.FindGeneratedType("InlineArrayIndexerExtensions").OfType<ClassDeclarationSyntax>().ToArray();
        Assert.Single(arrayHelpers.SelectMany(c => c.Members.OfType<MethodDeclarationSyntax>()), m => m.Identifier.ValueText == "SliceAtNull");
        Assert.Single(arrayHelpers.SelectMany(c => c.AttributeLists).SelectMany(a => a.Attributes));
        if (useComSourceGenerators)
        {
            this.RunSdkGenerators();
        }

        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsBaselineRemainsWithoutLaterSupplement(bool earlierSupplement)
    {
        this.generator = this.CreateSuperGenerator(
            earlierSupplement ? [AgilityPath, BaselinePath] : [BaselinePath],
            DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("DML_BUFFER_BINDING", TestContext.Current.CancellationToken));
        Assert.True(this.generator.TryGenerate("D3D12_FEATURE", TestContext.Current.CancellationToken));
        this.CollectGeneratedCode(this.generator);
        EnumDeclarationSyntax feature = Assert.Single(this.FindGeneratedType("D3D12_FEATURE").OfType<EnumDeclarationSyntax>());
        Assert.DoesNotContain(feature.Members, m => m.Identifier.ValueText == "D3D12_FEATURE_D3D12_OPTIONS22");
        Assert.Contains(this.generator.GetCompilationUnits(TestContext.Current.CancellationToken), unit =>
            unit.Key.StartsWith("Windows.Win32.", StringComparison.Ordinal) &&
            unit.Value.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(t => t.Identifier.ValueText == "ID3D12Resource"));
        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(Platform.AnyCpu)]
    [InlineData(Platform.X86)]
    [InlineData(Platform.X64)]
    [InlineData(Platform.Arm64)]
    public void PublicAgilitySupportsTargetArchitecture(Platform platform)
    {
        this.compilation = this.starterCompilations["net8.0"].WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateSuperGenerator([BaselinePath, AgilityPath], DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi("DML_BUFFER_BINDING");
        Assert.Single(this.FindGeneratedType("ID3D12Resource"));
    }

    private void RunSdkGenerators()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "ComSourceGenerators");
        ISourceGenerator[] generators = new[] { "Microsoft.Interop.ComInterfaceGenerator.dll", "Microsoft.Interop.LibraryImportGenerator.dll" }
            .SelectMany(name => Assembly.LoadFrom(Path.Combine(directory, name)).GetTypes())
            .Where(type => type.GetCustomAttribute<GeneratorAttribute>() is not null)
            .Select(type => ((IIncrementalGenerator)Activator.CreateInstance(type)!).AsSourceGenerator())
            .ToArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: this.parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(this.compilation, out Compilation updatedCompilation, out ImmutableArray<Diagnostic> diagnostics);
        Assert.Empty(diagnostics);
        ImmutableArray<SyntaxTree> generatedTrees = driver.GetRunResult().GeneratedTrees;
        Assert.NotEmpty(generatedTrees);
        Microsoft.CodeAnalysis.Emit.EmitResult emit = updatedCompilation.Emit(Stream.Null);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        this.compilation = (CSharpCompilation)updatedCompilation;
        foreach (SyntaxTree tree in generatedTrees)
        {
            // The SDK generator mixes line endings on Linux; validate untouched output before formatting checks.
            SyntaxTree normalized = CSharpSyntaxTree.ParseText(TestUtils.NormalizeToExpectedLineEndings(tree.ToString()), this.parseOptions, tree.FilePath);
            this.compilation = this.compilation.ReplaceSyntaxTree(tree, normalized);
        }
    }
}
