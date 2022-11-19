// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public abstract class GeneratorTestBase : IDisposable, IAsyncLifetime
{
    protected static readonly GeneratorOptions DefaultTestGeneratorOptions = new GeneratorOptions { EmitSingleFile = true };
    protected static readonly string FileSeparator = new string('=', 140);
    protected static readonly string MetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Win32.winmd");
    ////protected static readonly string DiaMetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Microsoft.Dia.winmd");
    protected static readonly string ApiDocsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "apidocs.msgpack");

    protected readonly ITestOutputHelper logger;
    protected readonly Dictionary<string, CSharpCompilation> starterCompilations = new();
    protected readonly Dictionary<string, ImmutableArray<string>> preprocessorSymbolsByTfm = new();
    protected CSharpCompilation compilation;
    protected CSharpParseOptions parseOptions;
    protected Generator? generator;

    public GeneratorTestBase(ITestOutputHelper logger)
    {
        this.logger = logger;

        this.parseOptions = CSharpParseOptions.Default
            .WithDocumentationMode(DocumentationMode.Diagnose)
            .WithLanguageVersion(LanguageVersion.CSharp9);

        // set in InitializeAsync
        this.compilation = null!;
    }

    public enum MarshalingOptions
    {
        NoMarshaling,
        MarshalingWithoutSafeHandles,
        FullMarshaling,
    }

    public static IEnumerable<object[]> TFMData =>
        new object[][]
        {
            new object[] { "net35" },
            new object[] { "net472" },
            new object[] { "netstandard2.0" },
            new object[] { "net6.0" },
        };

    public static IEnumerable<object[]> TFMDataNoNetFx35 =>
        new object[][]
        {
            new object[] { "net472" },
            new object[] { "netstandard2.0" },
            new object[] { "net6.0" },
        };

    public static Platform[] SpecificCpuArchitectures =>
        new Platform[]
        {
            Platform.X86,
            Platform.X64,
            Platform.Arm64,
        };

    public static Platform[] AnyCpuArchitectures =>
        new Platform[]
        {
            Platform.AnyCpu,
            Platform.X86,
            Platform.X64,
            Platform.Arm64,
        };

    public static IEnumerable<object[]> AvailableMacros => Generator.AvailableMacros.Select(name => new object[] { name });

    public async Task InitializeAsync()
    {
        this.starterCompilations.Add("net35", await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net35));
        this.starterCompilations.Add("net472", await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net472));
        this.starterCompilations.Add("netstandard2.0", await this.CreateCompilationAsync(MyReferenceAssemblies.NetStandard20));
        this.starterCompilations.Add("net6.0", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net60));
        this.starterCompilations.Add("net6.0-x86", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net60, Platform.X86));
        this.starterCompilations.Add("net6.0-x64", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net60, Platform.X64));
        this.starterCompilations.Add("net7.0", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net70));

        foreach (string tfm in this.starterCompilations.Keys)
        {
            if (tfm.StartsWith("net6") || tfm.StartsWith("net7"))
            {
                AddSymbols("NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET6_0");
            }

            if (tfm.StartsWith("net7"))
            {
                AddSymbols("NET7_0_OR_GREATER", "NET7_0");
            }

            // Guarantee we have at least an empty list of symbols for each TFM.
            AddSymbols();

            void AddSymbols(params string[] symbols)
            {
                if (!this.preprocessorSymbolsByTfm.TryAdd(tfm, symbols.ToImmutableArray()))
                {
                    this.preprocessorSymbolsByTfm[tfm] = this.preprocessorSymbolsByTfm[tfm].AddRange(symbols);
                }
            }
        }

        this.compilation = this.starterCompilations["netstandard2.0"];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        this.generator?.Dispose();
    }

    protected static AccessorDeclarationSyntax? FindAccessor(BaseTypeDeclarationSyntax typeSyntax, string propertyName, SyntaxKind kind)
    {
        SyntaxList<MemberDeclarationSyntax> members = typeSyntax switch
        {
            InterfaceDeclarationSyntax iface => iface.Members,
            StructDeclarationSyntax s => s.Members,
            _ => throw new NotSupportedException(),
        };
        PropertyDeclarationSyntax? property = members.OfType<PropertyDeclarationSyntax>().SingleOrDefault(p => p.Identifier.ValueText == propertyName);
        return FindAccessor(property, kind);
    }

    protected static AccessorDeclarationSyntax? FindAccessor(PropertyDeclarationSyntax? property, SyntaxKind kind) => property?.AccessorList?.Accessors.SingleOrDefault(a => a.IsKind(kind));

    protected static bool IsAttributePresent(AttributeListSyntax al, string attributeName) => al.Attributes.Any(a => a.Name.ToString() == attributeName);

    protected static IEnumerable<AttributeSyntax> FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name) => attributeLists.SelectMany(al => al.Attributes).Where(a => a.Name.ToString() == name);

    protected CSharpCompilation AddGeneratedCode(CSharpCompilation compilation, Generator generator)
    {
        var compilationUnits = generator.GetCompilationUnits(CancellationToken.None);
        var syntaxTrees = new List<SyntaxTree>(compilationUnits.Count);
        foreach (var unit in compilationUnits)
        {
            // Our syntax trees aren't quite right. And anyway the source generator API only takes text anyway so it doesn't _really_ matter.
            // So render the trees as text and have C# re-parse them so we get the same compiler warnings/errors that the user would get.
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(unit.Value.ToFullString(), this.parseOptions, path: unit.Key));
        }

        // Add namespaces that projects may define to ensure we prefix types with "global::" everywhere.
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.System { }", this.parseOptions, path: "Microsoft.System.cs"),
            CSharpSyntaxTree.ParseText("namespace Windows.Win32.System { }", this.parseOptions, path: "Windows.Win32.System.cs"));

        return compilation.AddSyntaxTrees(syntaxTrees);
    }

    protected void CollectGeneratedCode(Generator generator) => this.compilation = this.AddGeneratedCode(this.compilation, generator);

    protected IEnumerable<MethodDeclarationSyntax> FindGeneratedMethod(string name, Compilation? compilation = null) => (compilation ?? this.compilation).SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).Where(md => md.Identifier.ValueText == name);

    protected IEnumerable<BaseTypeDeclarationSyntax> FindGeneratedType(string name, Compilation? compilation = null) => (compilation ?? this.compilation).SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()).Where(btd => btd.Identifier.ValueText == name);

    protected IEnumerable<FieldDeclarationSyntax> FindGeneratedConstant(string name, Compilation? compilation = null) => (compilation ?? this.compilation).SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>()).Where(fd => (fd.Modifiers.Any(SyntaxKind.StaticKeyword) || fd.Modifiers.Any(SyntaxKind.ConstKeyword)) && fd.Declaration.Variables.Any(vd => vd.Identifier.ValueText == name));

    protected (FieldDeclarationSyntax Field, VariableDeclaratorSyntax Variable)? FindFieldDeclaration(TypeDeclarationSyntax type, string fieldName)
    {
        foreach (FieldDeclarationSyntax field in type.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
            {
                if (variable.Identifier.ValueText == fieldName)
                {
                    return (field, variable);
                }
            }
        }

        return null;
    }

    protected bool IsMethodGenerated(string name) => this.FindGeneratedMethod(name).Any();

    protected void AssertNoDiagnostics(bool logAllGeneratedCode = true) => this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode);

    protected void AssertNoDiagnostics(CSharpCompilation compilation, bool logAllGeneratedCode = true)
    {
        var diagnostics = FilterDiagnostics(compilation.GetDiagnostics());
        this.LogDiagnostics(diagnostics);

        var emitDiagnostics = ImmutableArray<Diagnostic>.Empty;
        bool? emitSuccessful = null;
        if (diagnostics.IsEmpty)
        {
            var emitResult = compilation.Emit(peStream: Stream.Null, xmlDocumentationStream: Stream.Null);
            emitSuccessful = emitResult.Success;
            emitDiagnostics = FilterDiagnostics(emitResult.Diagnostics);
            this.LogDiagnostics(emitDiagnostics);
        }

        if (logAllGeneratedCode)
        {
            this.LogGeneratedCode(compilation);
        }
        else
        {
            foreach (SyntaxTree? fileWithDiagnosticts in diagnostics.Select(d => d.Location.SourceTree).Distinct())
            {
                if (fileWithDiagnosticts is object)
                {
                    this.LogGeneratedCode(fileWithDiagnosticts);
                }
            }
        }

        Assert.Empty(diagnostics);
        if (emitSuccessful.HasValue)
        {
            Assert.Empty(emitDiagnostics);
            Assert.True(emitSuccessful);
        }

        AssertConsistentLineEndings(compilation);
    }

    protected void LogDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }
    }

    protected void LogGeneratedCode(CSharpCompilation compilation)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            this.LogGeneratedCode(tree);
        }
    }

    protected void LogGeneratedCode(SyntaxTree tree)
    {
        this.logger.WriteLine(FileSeparator);
        this.logger.WriteLine($"{tree.FilePath} content:");
        this.logger.WriteLine(FileSeparator);
        using var lineWriter = new NumberedLineWriter(this.logger);
        tree.GetRoot().WriteTo(lineWriter);
        lineWriter.WriteLine(string.Empty);
    }

    protected void AssertGeneratedType(string apiName, string expectedSyntax, string? expectedExtensions = null)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        BaseTypeDeclarationSyntax syntax = Assert.Single(this.FindGeneratedType(apiName));
        Assert.Equal(TestUtils.NormalizeToExpectedLineEndings(expectedSyntax), TestUtils.NormalizeToExpectedLineEndings(syntax.ToFullString()));

        var extensionsClass = (ClassDeclarationSyntax?)this.FindGeneratedType("InlineArrayIndexerExtensions").SingleOrDefault();
        if (expectedExtensions is string)
        {
            Assert.NotNull(extensionsClass);
            string extensionsClassString = extensionsClass!.ToFullString();
            Assert.Equal(TestUtils.NormalizeToExpectedLineEndings(expectedExtensions), TestUtils.NormalizeToExpectedLineEndings(extensionsClassString));
        }
        else
        {
            // Assert that no indexer was generated.
            Assert.Null(extensionsClass);
        }
    }

    protected void AssertGeneratedMember(string apiName, string memberName, string expectedSyntax)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        BaseTypeDeclarationSyntax typeSyntax = Assert.Single(this.FindGeneratedType(apiName));
        var semanticModel = this.compilation.GetSemanticModel(typeSyntax.SyntaxTree, ignoreAccessibility: false);
        var member = Assert.Single(semanticModel.GetDeclaredSymbol(typeSyntax, CancellationToken.None)!.GetMembers(memberName));
        var memberSyntax = member.DeclaringSyntaxReferences.Single().GetSyntax(CancellationToken.None);
        Assert.Equal(
            TestUtils.NormalizeToExpectedLineEndings(expectedSyntax).Trim(),
            TestUtils.NormalizeToExpectedLineEndings(memberSyntax.ToFullString()).Trim());
    }

    protected async Task<CSharpCompilation> CreateCompilationAsync(ReferenceAssemblies references, Platform platform = Platform.AnyCpu)
    {
        ImmutableArray<MetadataReference> metadataReferences = await references.ResolveAsync(LanguageNames.CSharp, default);

        // Workaround for https://github.com/dotnet/roslyn-sdk/issues/699
        metadataReferences = metadataReferences.AddRange(
            Directory.GetFiles(Path.Combine(Path.GetTempPath(), "test-packages", "Microsoft.Windows.SDK.Contracts.10.0.19041.1", "ref", "netstandard2.0"), "*.winmd").Select(p => MetadataReference.CreateFromFile(p)));

        // CONSIDER: How can I pass in the source generator itself, with AdditionalFiles, so I'm exercising that code too?
        var compilation = CSharpCompilation.Create(
            assemblyName: "test",
            references: metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform, allowUnsafe: true));

        return compilation;
    }

    protected Generator CreateGenerator(GeneratorOptions? options = null, CSharpCompilation? compilation = null, bool includeDocs = false) => this.CreateGenerator(MetadataPath, options, compilation, includeDocs);

    protected Generator CreateGenerator(string path, GeneratorOptions? options = null, CSharpCompilation? compilation = null, bool includeDocs = false) => new Generator(path, includeDocs ? Docs.Get(ApiDocsPath) : null, options ?? DefaultTestGeneratorOptions, compilation ?? this.compilation, this.parseOptions);

    private static ImmutableArray<Diagnostic> FilterDiagnostics(ImmutableArray<Diagnostic> diagnostics) => diagnostics.Where(d => d.Severity > DiagnosticSeverity.Hidden).ToImmutableArray();

    private static void AssertConsistentLineEndings(Compilation compilation)
    {
        foreach (SyntaxTree doc in compilation.SyntaxTrees)
        {
            AssertConsistentLineEndings(doc);
        }
    }

    private static void AssertConsistentLineEndings(SyntaxTree syntaxTree)
    {
        SourceText sourceText = syntaxTree.GetText();
        int firstLineBreakLength = default;
        int lineCount = 1;
        foreach (TextLine line in sourceText.Lines)
        {
            int thisLineBreakLength = line.EndIncludingLineBreak - line.End;
            if (lineCount == 1)
            {
                firstLineBreakLength = thisLineBreakLength;
            }
            else
            {
                if (firstLineBreakLength != thisLineBreakLength && thisLineBreakLength > 0)
                {
                    Assert.False(true, $"{syntaxTree.FilePath} Line {lineCount} had a {thisLineBreakLength}-byte line ending but line 1's line ending was {firstLineBreakLength} bytes long.");
                }
            }

            lineCount++;
        }
    }

    protected static class MyReferenceAssemblies
    {
#pragma warning disable SA1202 // Elements should be ordered by access - because field initializer depend on each other
        private static readonly ImmutableArray<PackageIdentity> AdditionalLegacyPackages = ImmutableArray.Create(
            new PackageIdentity("Microsoft.Windows.SDK.Contracts", "10.0.19041.1"));

        private static readonly ImmutableArray<PackageIdentity> AdditionalModernPackages = AdditionalLegacyPackages.AddRange(ImmutableArray.Create(
            new PackageIdentity("System.Runtime.CompilerServices.Unsafe", "6.0.0"),
            new PackageIdentity("System.Memory", "4.5.5"),
            new PackageIdentity("Microsoft.Win32.Registry", "5.0.0")));

        internal static readonly ReferenceAssemblies NetStandard20 = ReferenceAssemblies.NetStandard.NetStandard20.AddPackages(AdditionalModernPackages);
#pragma warning restore SA1202 // Elements should be ordered by access

        internal static class NetFramework
        {
            internal static readonly ReferenceAssemblies Net35 = ReferenceAssemblies.NetFramework.Net35.Default.AddPackages(AdditionalLegacyPackages);
            internal static readonly ReferenceAssemblies Net472 = ReferenceAssemblies.NetFramework.Net472.Default.AddPackages(AdditionalModernPackages);
        }

        internal static class Net
        {
            internal static readonly ReferenceAssemblies Net60 = ReferenceAssemblies.Net.Net60.AddPackages(AdditionalModernPackages);
            internal static readonly ReferenceAssemblies Net70 = ReferenceAssemblies.Net.Net70.AddPackages(AdditionalModernPackages);
        }
    }
}
