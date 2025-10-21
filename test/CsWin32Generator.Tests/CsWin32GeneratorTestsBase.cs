// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CsWin32Generator.Tests;

[Flags]
public enum TestOptions
{
    None = 0,
    GeneratesNothing = 1,
    DoNotFailOnDiagnostics = 2,
}

public partial class CsWin32GeneratorTestsBase : GeneratorTestBase
{
    protected bool fullGeneration;
    protected string? nativeMethodsTxt;
    protected List<string> nativeMethods = new();
    protected string? nativeMethodsJson;
    protected List<string> additionalReferences = new();
    protected string assemblyName = "TestAssembly";
    protected string? keyFile;
    protected string platform = "x64";

    public CsWin32GeneratorTestsBase(ITestOutputHelper logger)
        : base(logger)
    {
    }

    public ITestOutputHelper Logger => TestContext.Current.TestOutputHelper!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.compilation = this.starterCompilations["net9.0"];
    }

    // NOTE: Only use this method from a [Fact]. [Theory] should always use the below method to generate a unique testCase name per combination.
    protected async Task InvokeGeneratorAndCompileFromFact([CallerMemberName] string testCase = "")
    {
        await this.InvokeGeneratorAndCompile(testCase);
    }

    protected async Task InvokeGeneratorAndCompile(string testCase, TestOptions options = TestOptions.None)
    {
        this.compilation = this.compilation.AddReferences(this.additionalReferences.Select(x => MetadataReference.CreateFromFile(x)));

        string outputPath = this.GetTestCaseOutputDirectory(testCase);
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }

        await this.InvokeGenerator(outputPath, testCase, options);

        // Collect all generated .cs files from the output directory
        string[] generatedFiles = Directory.GetFiles(outputPath, "*.g.cs");

        this.Logger.WriteLine($"Found {generatedFiles.Length} generated files:");
        foreach (string file in generatedFiles)
        {
            this.Logger.WriteLine($"  - {Path.GetFileName(file)}");
        }

        // Create a compilation with the generated files
        if (generatedFiles.Length > 0)
        {
            await this.CompileGeneratedFilesWithSourceGenerators(outputPath, generatedFiles, options);
        }
    }

    protected async Task InvokeGenerator(string outputPath, string testCase, TestOptions options)
    {
        string nativeMethodsTxtPath;
        if (this.nativeMethodsTxt is string)
        {
            Assert.Empty(this.nativeMethods);
            nativeMethodsTxtPath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", this.nativeMethodsTxt);
        }
        else
        {
            nativeMethodsTxtPath = Path.Combine(this.GetTestCaseOutputDirectory(testCase), "NativeMethods.txt");
            File.WriteAllLines(nativeMethodsTxtPath, this.nativeMethods);
        }

        // Arrange
        string win32winmd = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "Windows.Win32.winmd");

        Directory.CreateDirectory(outputPath);

        this.Logger.WriteLine($"OutputPath: {outputPath}");

        List<string> args = new();
        args.AddRange(["--assembly-name", this.assemblyName]);
        args.AddRange(["--native-methods-txt", nativeMethodsTxtPath]);
        args.AddRange(["--metadata-paths", win32winmd]);
        args.AddRange(["--output-path", outputPath]);
        args.AddRange(["--platform", this.platform]);
        if (this.nativeMethodsJson is string)
        {
            string nativeMethodsJsonPath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", this.nativeMethodsJson);
            args.AddRange(["--native-methods-json", nativeMethodsJsonPath]);
        }

        foreach (MetadataReference reference in this.compilation.References)
        {
            if (reference is PortableExecutableReference peRef && peRef.FilePath is not null)
            {
                args.AddRange(["--references", peRef.FilePath]);
            }
        }

        foreach (string reference in this.additionalReferences)
        {
            args.AddRange(["--references", reference]);
        }

        if (this.keyFile is not null)
        {
            args.AddRange(["--key-file", this.keyFile]);
        }

        if (this.parseOptions.LanguageVersion is LanguageVersion version && version < LanguageVersion.CSharp13)
        {
            args.AddRange(["--language-version", LanguageVersionFacts.ToDisplayString(version)]);
        }

        // Act
        var loggerWriter = new TestOutputWriter(this.Logger);
        var program = new Program(loggerWriter, loggerWriter);
        int exitCode = await program.Main(args.ToArray(), this.fullGeneration);

        // Assert
        Assert.Equal(0, exitCode);
        if (!options.HasFlag(TestOptions.GeneratesNothing))
        {
            Assert.True(Directory.GetFiles(outputPath, "*.g.cs").Any(), "No generated files found.");
        }
    }

    protected async Task CompileGeneratedFilesWithSourceGenerators(string outputPath, string[] generatedFiles, TestOptions options)
    {
        this.Logger.WriteLine("Compiling generated files with source generators...");

        // Create syntax trees from the generated files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (string filePath in generatedFiles)
        {
            string sourceCode = await File.ReadAllTextAsync(filePath);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath, options: this.parseOptions);
            syntaxTrees.Add(syntaxTree);
        }

        // Add the assembly attribute for DisableRuntimeMarshalling
        string disableRuntimeMarshallingSource = @"
using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]
";
        SyntaxTree disableRuntimeMarshallingSyntaxTree = CSharpSyntaxTree.ParseText(disableRuntimeMarshallingSource, path: "DisableRuntimeMarshalling.cs", options: this.parseOptions);
        syntaxTrees.Add(disableRuntimeMarshallingSyntaxTree);

        this.compilation = this.compilation.AddSyntaxTrees(syntaxTrees);

        // Get source generators from the static analyzers list
        this.GetAvailableAnalyzers(out var sourceGenerators, out var diagnosticAnalyzers);

        List<Diagnostic> allDiagnostics = new();

        if (sourceGenerators.Any())
        {
            this.Logger.WriteLine($"Found {sourceGenerators.Count} source generators:");
            foreach (var generator in sourceGenerators)
            {
                this.Logger.WriteLine($"  - {generator.GetType().Name}");
            }

            // Create a GeneratorDriver with the source generators
            GeneratorDriver driver = CSharpGeneratorDriver.Create(sourceGenerators.Where(x => x is IIncrementalGenerator).Select(GeneratorExtensions.AsSourceGenerator), parseOptions: this.parseOptions);

            // Run the source generators
            var generatorDriver = driver.RunGeneratorsAndUpdateCompilation(this.compilation, out var newCompilation, out var generatorDiagnostics);
            this.compilation = (CSharpCompilation)newCompilation;

            // Write out all generated files
            foreach (SyntaxTree generatedFile in this.compilation.SyntaxTrees)
            {
                if (!Path.IsPathRooted(generatedFile.FilePath))
                {
                    string generatedFilePath = Path.Combine(outputPath, generatedFile.FilePath);
                    string? generatedFileDirectory = Path.GetDirectoryName(generatedFilePath);
                    if (generatedFileDirectory is not null && !Directory.Exists(generatedFileDirectory))
                    {
                        Directory.CreateDirectory(generatedFileDirectory);
                    }

                    this.Logger.WriteLine("Writing generated file: " + generatedFilePath);
                    File.WriteAllText(generatedFilePath, generatedFile.GetCompilationUnitRoot().GetText(Encoding.UTF8).ToString());
                }
            }

            // Filter out SYSLIB1092 diagnostics (related to DisableRuntimeMarshalling) as they are expected
            var filteredGeneratorDiagnostics = generatorDiagnostics.Where(d =>
                !(d.Descriptor.Id == "SYSLIB1092" && d.Descriptor.Title.ToString().Contains("The return value in the managed definition will be converted to an additional 'out' parameter at the end of the parameter list when calling the unmanaged COM method.", StringComparison.OrdinalIgnoreCase)));

            allDiagnostics.AddRange(filteredGeneratorDiagnostics);
        }
        else
        {
            this.Logger.WriteLine("No source generators found in the analyzers list");
        }

        CompilationWithAnalyzers compilationWithAnalyzers = this.compilation.WithAnalyzers(diagnosticAnalyzers.ToImmutableArray());

        // Get compilation diagnostics
        var analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();

        var filteredAnalyzerDiagnostics = analyzerDiagnostics.Where(d =>
            d.Descriptor.Id switch {
                "CA1016" or
                "SA1517" or
                "SA1633" or
                "CS1701" or
                "CA1418" or // Ignore bad platforms coming from win32metadata like "windowsserver2008"
                "CS0465" // IMFSinkWriterEx has a "Finalize" method
                => false, _ => true, });

        allDiagnostics.AddRange(filteredAnalyzerDiagnostics);

        // Log any diagnostics from source generation (that we aren't suppressing)
        foreach (var diagnostic in allDiagnostics)
        {
            this.Logger.WriteLine($"Diagnostic: {diagnostic}");
        }

        if (!options.HasFlag(TestOptions.DoNotFailOnDiagnostics))
        {
            Assert.Empty(allDiagnostics);

            // Optionally, emit the assembly to verify it's valid
            using var stream = new MemoryStream();
            var emitResult = this.compilation.Emit(stream);

            Assert.True(emitResult.Success, "Emitting the assembly failed.");
        }
    }

    protected void GetAvailableAnalyzers(out List<IIncrementalGenerator> generators, out List<DiagnosticAnalyzer> diagnosticAnalyzers)
    {
        generators = new();
        diagnosticAnalyzers = new();

        // Use the static analyzers list generated by MSBuild instead of GetCompilerReferences
        foreach (string analyzerPath in analyzers)
        {
            // Load the assembly from the analyzer path
            var assembly = System.Reflection.Assembly.LoadFrom(analyzerPath);

            // Look for types that have the [Generator] attribute
            var generatorTypes = assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(GeneratorAttribute), inherit: false).Length > 0)
                .Where(t => typeof(IIncrementalGenerator).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            foreach (var type in generatorTypes)
            {
                generators.Add((IIncrementalGenerator?)Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not construct {type} generator"));
            }

            var analyzerTypes = assembly.GetTypes()
                .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            foreach (var type in analyzerTypes)
            {
                diagnosticAnalyzers.Add((DiagnosticAnalyzer?)Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not construct {type} analyzer"));
            }
        }
    }

    protected string GetTestCaseOutputDirectory(string testCase)
    {
        string outputPath = Path.Combine(this.GetOutputDirectory(), testCase);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        return outputPath;
    }

    protected string GetOutputDirectory()
    {
        return Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestOutput");
    }

    private class TestOutputWriter : TextWriter
    {
        private ITestOutputHelper outputHelper;

        public TestOutputWriter(ITestOutputHelper outputHelper)
        {
            this.outputHelper = outputHelper;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char[] buffer, int index, int count)
        {
            this.outputHelper.Write(new string(buffer, index, count));
        }
    }
}
