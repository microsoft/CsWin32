// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsWin32Generator.Tests;

public partial class CsWin32GeneratorTests
{
    public CsWin32GeneratorTests()
    {
        string outputPath = this.GetOutputDirectory();
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }
    }

    public ITestOutputHelper Logger => TestContext.Current.TestOutputHelper!;

    [Fact]
    public async Task BasicNativeMethods()
    {
        await this.InvokeGenerator("BasicNativeMethods.txt");
    }

    [Fact]
    public async Task Generate_RmRegisterResources()
    {
        string nativeMethodsTxt = Path.Combine(this.GetTestCaseOutputDirectory(), "RmRegisterResources.txt");
        File.WriteAllLines(nativeMethodsTxt, ["RmRegisterResources"]);

        await this.InvokeGenerator(nativeMethodsTxt);

        // Collect all generated .cs files from the output directory
        string outputPath = this.GetTestCaseOutputDirectory();
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

    [Fact]
    public async Task TestNativeMethods()
    {
        await this.InvokeGenerator("NativeMethods.txt", "NativeMethods.json");
    }

    [Fact]
    public async Task CommandLineTool_ShowsError_WhenNativeMethodsTxtMissing()
    {
        // Arrange
        string missingNativeMethodsTxtPath = Path.Combine("test", "NonExistent", "NativeMethods.txt");
        string outputPath = Path.Combine(Path.GetTempPath(), "CsWin32GeneratorTests_Output3");
        Directory.CreateDirectory(outputPath);

        // Act
        int exitCode = await CsWin32Generator.Program.Main(new[]
        {
            "--native-methods-txt", missingNativeMethodsTxtPath,
            "--output-path", outputPath,
        });

        // Assert
        Assert.NotEqual(0, exitCode);
    }

    private async Task CompileGeneratedFilesWithSourceGenerators(string[] generatedFiles)
    {
        this.Logger.WriteLine("Compiling generated files with source generators...");

        // Create syntax trees from the generated files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (string filePath in generatedFiles)
        {
            string sourceCode = await File.ReadAllTextAsync(filePath);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            syntaxTrees.Add(syntaxTree);
        }

        // Get references from the current compilation context - use the same references
        // that are available to the test project
        var references = this.GetCompilerReferences()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        // Create compilation options
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true,
            platform: Platform.X64);

        // Create the compilation
        var compilation = CSharpCompilation.Create(
            "GeneratedCode",
            syntaxTrees,
            references,
            compilationOptions);

        // Get source generators from the static analyzers list
        var sourceGenerators = this.GetAvailableSourceGenerators();

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
            var generatorDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);
            compilation = (CSharpCompilation)newCompilation;

            // Log any diagnostics from source generation
            foreach (var diagnostic in diagnostics)
            {
                this.Logger.WriteLine($"Source generator diagnostic: {diagnostic}");
            }
        }
        else
        {
            this.Logger.WriteLine("No source generators found in the analyzers list");
        }

        // Get compilation diagnostics
        var compilationDiagnostics = compilation.GetDiagnostics();

        // Log diagnostics
        foreach (var diagnostic in compilationDiagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                this.Logger.WriteLine($"Compilation error: {diagnostic}");
            }
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                this.Logger.WriteLine($"Compilation warning: {diagnostic}");
            }
        }

        // Check for compilation success
        bool hasErrors = compilationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(!hasErrors, "Compilation failed due to errors.");

        // Optionally, emit the assembly to verify it's valid
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);

        Assert.True(emitResult.Success, "Emitting the assembly failed.");
    }

    private IReadOnlyList<object> GetAvailableSourceGenerators()
    {
        var generators = new List<object>();

        // Use the static analyzers list generated by MSBuild instead of GetCompilerReferences
        foreach (string analyzerPath in analyzers)
        {
            // Load the assembly from the analyzer path
            var assembly = System.Reflection.Assembly.LoadFrom(analyzerPath);

            // Look for types that have the [Generator] attribute
            var generatorTypes = assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(GeneratorAttribute), inherit: false).Length > 0)
                .ToList();

            foreach (var type in generatorTypes)
            {
                generators.Add(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not construct {type} generator"));
            }
        }

        return generators;
    }

    private async Task InvokeGenerator(string nativeMethodsTxt, string? nativeMethodsJson = null, [CallerMemberName] string testCase = "")
    {
        Console.SetOut(new TestOutputWriter(this.Logger));

        // Arrange
        string nativeMethodsTxtPath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", nativeMethodsTxt);
        string win32winmd = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "Windows.Win32.winmd");
        string outputPath = this.GetTestCaseOutputDirectory(testCase);

        Directory.CreateDirectory(outputPath);

        this.Logger.WriteLine($"OutputPath: {outputPath}");

        List<string> args = new();
        args.AddRange(["--native-methods-txt", nativeMethodsTxtPath]);
        args.AddRange(["--metadata-paths", win32winmd]);
        args.AddRange(["--output-path", outputPath]);
        args.AddRange(["--platform", "x64"]);
        if (nativeMethodsJson is string)
        {
            string nativeMethodsJsonPath = Path.Combine(Path.GetDirectoryName(typeof(CsWin32GeneratorTests).Assembly.Location)!, "TestContent", nativeMethodsJson);
            args.AddRange(["--native-methods-json", nativeMethodsJsonPath]);
        }

        foreach (System.Reflection.Assembly reference in this.GetCompilerReferences())
        {
            args.AddRange(["--references", reference.Location]);
        }

        this.Logger.WriteLine($"CsWin32Generator {string.Join(" ", args)}");

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

    private string GetTestCaseOutputDirectory([CallerMemberName] string testCase = "")
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
