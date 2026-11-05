// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;

public class SplitMetadataTests : GeneratorTestBase
{
    private static readonly string BaselineD3DPath = Path.Combine(Path.GetDirectoryName(MetadataPath)!, "Microsoft.Direct3D.D3D12.winmd");

    public SplitMetadataTests(ITestOutputHelper logger)
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
    public void PublicSdkProjectsAcrossAssemblies(bool allowMarshaling, bool useComSourceGenerators, bool reverseRequests)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string providerPath = BaselineD3DPath;
        this.generator = this.CreateSuperGenerator(
            reverseRequests ? [providerPath, MetadataPath] : [MetadataPath, providerPath],
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
            "D3D12_DRED_ALLOCATION_NODE1", "ID3D12DeviceRemovedExtendedData1",
            "ID3D12Device14", "ID3D12VirtualizationGuestDevice",
        ];
        if (reverseRequests)
        {
            Array.Reverse(requests);
        }

        foreach (string request in requests)
        {
            Assert.True(this.generator.TryGenerate(request, TestContext.Current.CancellationToken), request);
        }

        string[] snapshot = this.generator.GetCompilationUnits(CancellationToken.None).OrderBy(unit => unit.Key).Select(unit => unit.Value.ToFullString()).ToArray();
        Assert.Equal(snapshot, this.generator.GetCompilationUnits(CancellationToken.None).OrderBy(unit => unit.Key).Select(unit => unit.Value.ToFullString()));
        this.CollectGeneratedCode(this.generator);
        Assert.Single(this.FindGeneratedType("ID3D12Resource"));
        Assert.Single(this.FindGeneratedType("__float_4"));
        EnumDeclarationSyntax feature = Assert.Single(this.FindGeneratedType("D3D12_FEATURE").OfType<EnumDeclarationSyntax>());
        using Stream sdkStream = File.OpenRead(providerPath);
        using PEReader sdkPe = new(sdkStream);
        MetadataReader reader = sdkPe.GetMetadataReader();
        TypeDefinition enumDefinition = reader.GetTypeDefinition(reader.TypeDefinitions.Single(h => reader.StringComparer.Equals(reader.GetTypeDefinition(h).Name, "D3D12_FEATURE")));
        string[] expectedMembers = enumDefinition.GetFields().Select(h => reader.GetFieldDefinition(h))
            .Where(f => !f.GetDefaultValue().IsNil).Select(f => reader.GetString(f.Name)).ToArray();
        Assert.Equal(expectedMembers, feature.Members.Select(m => m.Identifier.ValueText));
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
                    Windows.Win32.Graphics.Direct3D12.D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS;
                internal static Windows.Win32.AI.MachineLearning.DirectML.DML_BUFFER_BINDING Bind(
                    Windows.Win32.Graphics.Direct3D12.{{resourceType}} resource) =>
                    new Windows.Win32.AI.MachineLearning.DirectML.DML_BUFFER_BINDING { Buffer = resource };
                internal static float Border(in Windows.Win32.Graphics.Direct3D11.D3D11_SAMPLER_DESC sampler) =>
                    Windows.Win32.InlineArrayIndexerExtensions.ReadOnlyItemRef(in sampler.BorderColor, 0);
                internal static float Border(in Windows.Win32.Graphics.Direct3D12.D3D12_SAMPLER_DESC sampler) =>
                    Windows.Win32.InlineArrayIndexerExtensions.ReadOnlyItemRef(in sampler.BorderColor, 0);
            }
            """);
        Assert.Equal(0, this.compilation.GetTypeByMetadataName("Consumer")!.GetMembers("NewFeature").OfType<IFieldSymbol>().Single().ConstantValue);
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
    public void RecursiveDredStructuresRetainManagedFields(bool reverseRequests)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string providerPath = BaselineD3DPath;
        this.generator = this.CreateSuperGenerator(
            reverseRequests ? [providerPath, MetadataPath] : [MetadataPath, providerPath],
            DefaultTestGeneratorOptions with { AllowMarshaling = true, ComInterop = new() { UseComSourceGenerators = false } });
        string[] requests =
        [
            "D3D12_DRED_ALLOCATION_NODE1", "D3D12_DRED_PAGE_FAULT_OUTPUT1", "D3D12_DRED_PAGE_FAULT_OUTPUT2",
            "D3D12_DEVICE_REMOVED_EXTENDED_DATA2", "D3D12_DEVICE_REMOVED_EXTENDED_DATA3",
            "D3D12_VERSIONED_DEVICE_REMOVED_EXTENDED_DATA", "ID3D12DeviceRemovedExtendedData1", "ID3D12DeviceRemovedExtendedData2",
        ];
        if (reverseRequests)
        {
            Array.Reverse(requests);
        }

        foreach (string request in requests)
        {
            Assert.True(this.generator.TryGenerate(request, TestContext.Current.CancellationToken), request);
        }

        this.CollectGeneratedCode(this.generator);
        this.compilation = this.AddCode(
            """
            internal static unsafe class DredConsumer
            {
                internal static Windows.Win32.Graphics.Direct3D12.D3D12_DRED_ALLOCATION_NODE1 Node(
                    Windows.Win32.System.Com.IUnknown obj) => new() { pObject = obj };
                internal static Windows.Win32.Graphics.Direct3D12.D3D12_DRED_ALLOCATION_NODE1_unmanaged Node(
                    Windows.Win32.System.Com.IUnknown* obj) => new() { pObject = obj };
            }
            """);
        this.AssertNoDiagnostics();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedHandleOverloadsRetainSafeFileHandle(bool reverseRequests)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateSuperGenerator(
            reverseRequests ? [BaselineD3DPath, MetadataPath] : [MetadataPath, BaselineD3DPath],
            DefaultTestGeneratorOptions with { AllowMarshaling = true, ComInterop = new() { UseComSourceGenerators = false } });
        string[] requests = ["ID3D12Device", "ID3D12Device14", "ID3D12VirtualizationGuestDevice"];
        if (reverseRequests)
        {
            Array.Reverse(requests);
        }

        foreach (string request in requests)
        {
            Assert.True(this.generator.TryGenerate(request, TestContext.Current.CancellationToken), request);
        }

        this.CollectGeneratedCode(this.generator);
        this.compilation = this.AddCode(
            """
            using Microsoft.Win32.SafeHandles;
            using Windows.Win32;
            using Windows.Win32.Graphics.Direct3D12;

            internal static class SharedHandleConsumer
            {
                internal static void Open(ID3D12Device device)
                {
                    device.OpenSharedHandleByName("shared-resource", 0, out SafeFileHandle handle);
                    handle.Dispose();
                }
                internal static void Create(ID3D12Device14 device, ID3D12DeviceChild resource)
                {
                    device.CreateSharedHandle(resource, null, 0, "shared-resource", out SafeFileHandle handle);
                    handle.Dispose();
                    device.OpenSharedHandleByName("shared-resource", 0, out handle);
                    handle.Dispose();
                }
                internal static void Share(ID3D12VirtualizationGuestDevice device, ID3D12DeviceChild resource)
                {
                    device.ShareWithHost(resource, out SafeFileHandle handle);
                    handle.Dispose();
                }
            }
            """);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void FailedCrossAssemblyGenerationCanRetry()
    {
        GeneratorOptions options = DefaultTestGeneratorOptions with { AllowMarshaling = false };
        using Generator provider = new(BaselineD3DPath, null, [], options, this.compilation, this.parseOptions);
        Assert.ThrowsAny<GenerationFailedException>(() => provider.TryGenerate("ID3D12Resource", CancellationToken.None));
        Assert.Empty(provider.GetCompilationUnits(CancellationToken.None));
        using Generator windows = new(MetadataPath, null, [], options, this.compilation, this.parseOptions);
        this.generator = SuperGenerator.Combine(provider, windows);
        Assert.True(this.generator.TryGenerate("ID3D12Resource", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void SplitMetadataHasExplicitDisjointOwnership()
    {
        using PEReader windows = new(File.OpenRead(MetadataPath));
        MetadataReader windowsReader = windows.GetMetadataReader();
        Assert.Equal(new Version(1, 0, 0, 0), windowsReader.GetAssemblyDefinition().Version);
        Assert.True(windowsReader.GetAssemblyDefinition().PublicKey.IsNil);
        Assert.DoesNotContain(windowsReader.TypeDefinitions, handle =>
            windowsReader.GetString(windowsReader.GetTypeDefinition(handle).Namespace) == "Windows.Win32.Graphics.Direct3D12");
        TypeDefinition binding = windowsReader.GetTypeDefinition(windowsReader.TypeDefinitions.Single(handle =>
            windowsReader.StringComparer.Equals(windowsReader.GetTypeDefinition(handle).Name, "DML_BUFFER_BINDING")));
        FieldDefinition buffer = windowsReader.GetFieldDefinition(binding.GetFields().Single(handle =>
            windowsReader.StringComparer.Equals(windowsReader.GetFieldDefinition(handle).Name, "Buffer")));
        BlobReader signature = windowsReader.GetBlobReader(buffer.Signature);
        Assert.Equal(SignatureKind.Field, signature.ReadSignatureHeader().Kind);
        Assert.Equal(SignatureTypeCode.TypeHandle, signature.ReadSignatureTypeCode());
        EntityHandle resourceHandle = signature.ReadTypeHandle();
        Assert.Equal(HandleKind.TypeReference, resourceHandle.Kind);
        TypeReference resource = windowsReader.GetTypeReference((TypeReferenceHandle)resourceHandle);
        Assert.Equal("ID3D12Resource", windowsReader.GetString(resource.Name));
        Assert.Equal(HandleKind.AssemblyReference, resource.ResolutionScope.Kind);
        AssemblyReference scope = windowsReader.GetAssemblyReference((AssemblyReferenceHandle)resource.ResolutionScope);
        Assert.Equal("Microsoft.Direct3D.D3D12.winmd", windowsReader.GetString(scope.Name));
        Assert.Equal(new Version(1, 0, 0, 0), scope.Version);

        static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil
                ? $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}"
                : $"{TypeName(reader, type.GetDeclaringType())}+{reader.GetString(type.Name)}";
        }

        HashSet<string> windowsTypes = windowsReader.TypeDefinitions
            .Where(handle => windowsReader.GetString(windowsReader.GetTypeDefinition(handle).Name) is not "Apis" and not "<Module>")
            .Select(handle => TypeName(windowsReader, handle)).ToHashSet();
        using (PEReader provider = new(File.OpenRead(BaselineD3DPath)))
        {
            MetadataReader reader = provider.GetMetadataReader();
            Assert.Equal("Microsoft.Direct3D.D3D12.winmd", reader.GetString(reader.GetAssemblyDefinition().Name));
            Assert.Equal(new Version(1, 0, 0, 0), reader.GetAssemblyDefinition().Version);
            Assert.True(reader.GetAssemblyDefinition().PublicKey.IsNil);
            Assert.DoesNotContain(reader.TypeDefinitions.Select(handle => TypeName(reader, handle)), windowsTypes.Contains);
            TypeDefinition resourceType = reader.GetTypeDefinition(reader.TypeDefinitions.Single(handle =>
                reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Name, "ID3D12Resource")));
            Assert.Equal(7, resourceType.GetMethods().Count);
        }
    }

    [Theory]
    [InlineData(Platform.AnyCpu)]
    [InlineData(Platform.X86)]
    [InlineData(Platform.X64)]
    [InlineData(Platform.Arm64)]
    public void SplitMetadataSupportsTargetArchitecture(Platform platform)
    {
        this.compilation = this.starterCompilations["net8.0"].WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateSuperGenerator([MetadataPath, BaselineD3DPath], DefaultTestGeneratorOptions with { AllowMarshaling = false });
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
