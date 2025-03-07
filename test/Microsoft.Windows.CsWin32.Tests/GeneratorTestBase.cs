// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

public abstract class GeneratorTestBase : IDisposable, IAsyncLifetime
{
    protected const string DefaultTFM = "netstandard2.0";
    protected static readonly GeneratorOptions DefaultTestGeneratorOptions = new GeneratorOptions { EmitSingleFile = true };
    protected static readonly string FileSeparator = new string('=', 140);
    protected static readonly string MetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Win32.winmd");
    protected static readonly string WdkMetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Wdk.winmd");
    protected static readonly string[] DefaultMetadataPaths = new[] { MetadataPath, WdkMetadataPath };
    ////protected static readonly string DiaMetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Microsoft.Dia.winmd");
    protected static readonly string ServiceFabricMetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "ExternalMetadata", "ServiceFabric.winmd");
    protected static readonly string ApiDocsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "apidocs.msgpack");

    protected readonly ITestOutputHelper logger;
    protected readonly Dictionary<string, CSharpCompilation> starterCompilations = new();
    protected readonly Dictionary<string, ImmutableArray<string>> preprocessorSymbolsByTfm = new();
    protected CSharpCompilation compilation;
    protected CSharpParseOptions parseOptions;
    protected IGenerator? generator;

    public GeneratorTestBase(ITestOutputHelper logger)
    {
        this.logger = logger;

        this.parseOptions = CSharpParseOptions.Default
            .WithDocumentationMode(DocumentationMode.Diagnose)
            .WithLanguageVersion(LanguageVersion.CSharp12);

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
            new object[] { "net8.0" },
            new object[] { "net9.0" },
        };

    public static IEnumerable<object[]> TFMDataNoNetFx35MemberData => TFMDataNoNetFx35.Select(tfm => new object[] { tfm }).ToArray();

    public static string[] TFMDataNoNetFx35 =>
        new string[]
        {
            "net472",
            "netstandard2.0",
            "net8.0",
            "net9.0",
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

    public async ValueTask InitializeAsync()
    {
        this.starterCompilations.Add("net35", await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net35));
        this.starterCompilations.Add("net472", await this.CreateCompilationAsync(MyReferenceAssemblies.NetFramework.Net472));
        this.starterCompilations.Add("netstandard2.0", await this.CreateCompilationAsync(MyReferenceAssemblies.NetStandard20));
        this.starterCompilations.Add("net8.0", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net80));
        this.starterCompilations.Add("net8.0-x86", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net80, Platform.X86));
        this.starterCompilations.Add("net8.0-x64", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net80, Platform.X64));
        this.starterCompilations.Add("net9.0", await this.CreateCompilationAsync(MyReferenceAssemblies.Net.Net90));

        foreach (string tfm in this.starterCompilations.Keys)
        {
            if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                AddSymbols("NETSTANDARD");
                AddSymbols("NETSTANDARD2_0");
            }
            else if (tfm.Contains('.'))
            {
                AddSymbols("NET5_0_OR_GREATER");
                AddSymbols("NET6_0_OR_GREATER");
                AddSymbols("NET7_0_OR_GREATER");
                AddSymbols("NET8_0_OR_GREATER");
                AddSymbols(tfm.Replace('.', '_').ToUpperInvariant());
            }
            else
            {
                AddSymbols("NETFRAMEWORK");
            }

            if (tfm.StartsWith("net9"))
            {
                AddSymbols("NET9_0_OR_GREATER");
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

        this.compilation = this.starterCompilations[DefaultTFM];
    }

    public ValueTask DisposeAsync()
    {
        this.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        this.generator?.Dispose();
    }

    internal static void LogGeneratedCode(SyntaxTree tree, ITestOutputHelper logger)
    {
        logger.WriteLine(FileSeparator);
        logger.WriteLine($"{tree.FilePath} content:");
        logger.WriteLine(FileSeparator);
        using NumberedLineWriter lineWriter = new(logger);
        tree.GetRoot().WriteTo(lineWriter);
        lineWriter.WriteLine(string.Empty);
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

    protected static bool IsOrContainsExternMethod(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(SyntaxKind.ExternKeyword) || method.Body?.Statements.OfType<LocalFunctionStatementSyntax>().Any(f => f.Modifiers.Any(SyntaxKind.ExternKeyword)) is true;
    }

    protected static LanguageVersion? GetLanguageVersionForTfm(string tfm) => tfm switch
    {
        "net8.0" => LanguageVersion.CSharp12,
        "net9.0" => LanguageVersion.CSharp13,
        _ => null,
    };

    protected static IEnumerable<AttributeSyntax> FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name) => attributeLists.SelectMany(al => al.Attributes).Where(a => a.Name.ToString() == name);

    protected void GenerateApi(string apiName)
    {
        this.generator ??= this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    protected CSharpCompilation AddGeneratedCode(CSharpCompilation compilation, IGenerator generator)
    {
        var compilationUnits = generator.GetCompilationUnits(CancellationToken.None).ToList();
        var syntaxTrees = new List<SyntaxTree>(compilationUnits.Count);
        foreach (var unit in compilationUnits)
        {
            // Our syntax trees aren't quite right. And anyway the source generator API only takes text anyway so it doesn't _really_ matter.
            // So render the trees as text and have C# re-parse them so we get the same compiler warnings/errors that the user would get.
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(unit.Value.GetText(Encoding.UTF8), this.parseOptions, path: unit.Key));
        }

        // Add namespaces that projects may define to ensure we prefix types with "global::" everywhere.
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.System { }", this.parseOptions, path: "Microsoft.System.cs"),
            CSharpSyntaxTree.ParseText("namespace Windows.Win32.System { }", this.parseOptions, path: "Windows.Win32.System.cs"));

        this.logger.WriteLine($"Emitted {syntaxTrees.Count:n0} syntax trees totalling {syntaxTrees.Sum(st => st.Length):n0} in size.");

        this.logger.WriteLine("The largest syntax trees are:");
        foreach (SyntaxTree st in syntaxTrees.OrderByDescending(st => st.Length).Take(5))
        {
            this.logger.WriteLine($"{st.Length,11:n0} {st.FilePath}");
        }

        return compilation.AddSyntaxTrees(syntaxTrees);
    }

    /// <summary>
    /// Adds a code file to a compilation.
    /// </summary>
    /// <param name="code">The syntax file to add.</param>
    /// <param name="fileName">The name of the code file to add.</param>
    /// <param name="compilation">The compilation to add to. When omitted, <see cref="GeneratorTestBase.compilation"/> is assumed.</param>
    /// <returns>The modified compilation.</returns>
    protected CSharpCompilation AddCode([StringSyntax("c#-test")] string code, string? fileName = null, CSharpCompilation? compilation = null)
    {
        compilation ??= this.compilation;
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code, this.parseOptions, fileName ?? $"AdditionalCode{compilation.SyntaxTrees.Length + 1}.cs");
        return compilation.AddSyntaxTrees(syntaxTree);
    }

    protected void CollectGeneratedCode(IGenerator generator) => this.compilation = this.AddGeneratedCode(this.compilation, generator);

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

    protected void AssertNoDiagnostics(CSharpCompilation compilation, bool logAllGeneratedCode = true, Func<Diagnostic, bool>? acceptable = null)
    {
        var diagnostics = FilterDiagnostics(compilation.GetDiagnostics());
        this.logger.WriteLine($"{diagnostics.Length} diagnostics reported.");
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

        Assert.Empty(acceptable is null ? diagnostics : diagnostics.Where(d => !acceptable(d)));
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

    protected void LogGeneratedCode(SyntaxTree tree) => LogGeneratedCode(tree, this.logger);

    protected void AssertGeneratedType(string apiName, string expectedSyntax, string? expectedExtensions = null)
    {
        this.GenerateApi(apiName);
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
        this.GenerateApi(apiName);
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
        const string winRTPackageId = "Microsoft.Windows.SDK.Contracts";
        var winRTPackage = references.Packages.SingleOrDefault(id => string.Equals(id.Id, winRTPackageId, StringComparison.OrdinalIgnoreCase));
        if (winRTPackage is not null)
        {
            metadataReferences = metadataReferences.AddRange(
                Directory.GetFiles(Path.Combine(Path.GetTempPath(), "test-packages", $"{winRTPackageId}.{winRTPackage.Version}", "ref", "netstandard2.0"), "*.winmd").Select(p => MetadataReference.CreateFromFile(p)));
        }

        // QUESTION: How can I pass in the source generator itself, with AdditionalFiles, so I'm exercising that code too?
        // ANSWER: Follow the pattern now used in SourceGeneratorTests.cs
        var compilation = CSharpCompilation.Create(
            assemblyName: "test",
            references: metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform, allowUnsafe: true));

        return compilation;
    }

    protected SuperGenerator CreateGenerator(GeneratorOptions? options = null, CSharpCompilation? compilation = null, bool includeDocs = false)
        => this.CreateSuperGenerator(DefaultMetadataPaths, options, compilation, includeDocs);

    protected SuperGenerator CreateSuperGenerator(string[] metadataPaths, GeneratorOptions? options = null, CSharpCompilation? compilation = null, bool includeDocs = false) =>
        SuperGenerator.Combine(metadataPaths.Select(path => new Generator(path, includeDocs ? Docs.Get(ApiDocsPath) : null, [], options ?? DefaultTestGeneratorOptions, compilation ?? this.compilation, this.parseOptions)));

    private static ImmutableArray<Diagnostic> FilterDiagnostics(ImmutableArray<Diagnostic> diagnostics) => diagnostics.Where(d => d.Severity > DiagnosticSeverity.Hidden && d.Descriptor.Id != "CS1701").ToImmutableArray();

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
                    Assert.Fail($"{syntaxTree.FilePath} Line {lineCount} had a {thisLineBreakLength}-byte line ending but line 1's line ending was {firstLineBreakLength} bytes long.");
                }
            }

            lineCount++;
        }
    }
}
