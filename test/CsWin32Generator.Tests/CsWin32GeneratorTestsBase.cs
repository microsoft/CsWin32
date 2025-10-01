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

public partial class CsWin32GeneratorTestsBase : GeneratorTestBase
{
    protected bool fullGeneration;
    protected string? nativeMethodsTxt;
    protected List<string> nativeMethods = new();
    protected string? nativeMethodsJson;

    public CsWin32GeneratorTestsBase(ITestOutputHelper logger)
        : base(logger)
    {
    }

    public ITestOutputHelper Logger => TestContext.Current.TestOutputHelper!;

    protected async Task InvokeGeneratorAndCompile([CallerMemberName] string testCase = "")
    {
        await this.InvokeGenerator(testCase);

        // Collect all generated .cs files from the output directory
        string outputPath = this.GetTestCaseOutputDirectory(testCase);
        string[] generatedFiles = Directory.GetFiles(outputPath, "*.g.cs");

        this.Logger.WriteLine($"Found {generatedFiles.Length} generated files:");
        foreach (string file in generatedFiles)
        {
            this.Logger.WriteLine($"  - {Path.GetFileName(file)}");
        }

        // Create a compilation with the generated files
        if (generatedFiles.Length > 0)
        {
            await this.CompileGeneratedFilesWithSourceGenerators(generatedFiles);
        }
    }

    protected async Task InvokeGenerator(string testCase)
    {
        string outputPath = this.GetTestCaseOutputDirectory(testCase);
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }

        Console.SetOut(new TestOutputWriter(this.Logger));

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
        args.AddRange(["--native-methods-txt", nativeMethodsTxtPath]);
        args.AddRange(["--metadata-paths", win32winmd]);
        args.AddRange(["--output-path", outputPath]);
        args.AddRange(["--platform", "x64"]);
        if (this.nativeMethodsJson is string)
        {
            string nativeMethodsJsonPath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", this.nativeMethodsJson);
            args.AddRange(["--native-methods-json", nativeMethodsJsonPath]);
        }

        if (this.fullGeneration)
        {
            CsWin32Generator.Program.FullGeneration = true;
        }

        CSharpCompilation baseCompilation = this.starterCompilations["net9.0"];
        foreach (MetadataReference reference in baseCompilation.References)
        {
            if (reference is PortableExecutableReference peRef && peRef.FilePath is not null)
            {
                args.AddRange(["--references", peRef.FilePath]);
            }
        }

        // Also add the WinRT.Runtime.dll reference
        foreach (string assemblyPath in winrtReferences)
        {
            args.AddRange(["--references", assemblyPath]);
        }

        // Act
        int exitCode = await CsWin32Generator.Program.Main(args.ToArray());

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(Directory.GetFiles(outputPath, "*.g.cs").Any(), "No generated files found.");
    }

    private IEnumerable<System.Reflection.Assembly> GetCompilerReferences()
    {
        return AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
    }

    private async Task CompileGeneratedFilesWithSourceGenerators(string[] generatedFiles)
    {
        this.Logger.WriteLine("Compiling generated files with source generators...");

        this.compilation = this.starterCompilations["net9.0"];

        this.compilation = this.compilation.AddReferences(winrtReferences.Select(x => MetadataReference.CreateFromFile(x)));

        // Create syntax trees from the generated files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (string filePath in generatedFiles)
        {
            string sourceCode = await File.ReadAllTextAsync(filePath);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            syntaxTrees.Add(syntaxTree);
        }

        // Add the assembly attribute for DisableRuntimeMarshalling
        string disableRuntimeMarshallingSource = @"
using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]
";
        SyntaxTree disableRuntimeMarshallingSyntaxTree = CSharpSyntaxTree.ParseText(disableRuntimeMarshallingSource, path: "DisableRuntimeMarshalling.cs");
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
            var driver = CSharpGeneratorDriver.Create(sourceGenerators.Where(x => x is IIncrementalGenerator).Select(x => (IIncrementalGenerator)x).ToArray());

            // Run the source generators
            var generatorDriver = driver.RunGeneratorsAndUpdateCompilation(this.compilation, out var newCompilation, out var generatorDiagnostics);
            this.compilation = (CSharpCompilation)newCompilation;

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
            d.Descriptor.Id switch { "CA1016" or "SA1517" or "SA1633" => false, _ => true });

        allDiagnostics.AddRange(filteredAnalyzerDiagnostics);

        // Log any diagnostics from source generation (that we aren't suppressing)
        foreach (var diagnostic in allDiagnostics)
        {
            this.Logger.WriteLine($"Diagnostic: {diagnostic}");
        }

        Assert.Empty(allDiagnostics);

        // Optionally, emit the assembly to verify it's valid
        using var stream = new MemoryStream();
        var emitResult = this.compilation.Emit(stream);

        Assert.True(emitResult.Success, "Emitting the assembly failed.");
    }

    private void GetAvailableAnalyzers(out List<IIncrementalGenerator> generators, out List<DiagnosticAnalyzer> diagnosticAnalyzers)
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

    private string GetTestCaseOutputDirectory(string testCase)
    {
        string outputPath = Path.Combine(this.GetOutputDirectory(), testCase);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        return outputPath;
    }

    private string GetOutputDirectory()
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
