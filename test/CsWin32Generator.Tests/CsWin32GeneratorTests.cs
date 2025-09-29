// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsWin32Generator.Tests;

public partial class CsWin32GeneratorTests : GeneratorTestBase
{
    private string? nativeMethodsTxt;
    private List<string> nativeMethods = new();
    private string? nativeMethodsJson;

    public CsWin32GeneratorTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    public ITestOutputHelper Logger => TestContext.Current.TestOutputHelper!;

    [Fact]
    public async Task BasicNativeMethods()
    {
        this.nativeMethods.Add("CHAR");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task Generate_RmRegisterResources()
    {
        this.nativeMethods.Add("RmRegisterResources");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task TestGenerateIDispatch()
    {
        this.nativeMethods.Add("IDispatch");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task TestArrayMarshalling()
    {
        this.nativeMethods.Add("IEnumEventObject");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task TestPropertyOnInterface()
    {
        this.nativeMethods.Add("IShellWindows");
        await this.InvokeGeneratorAndCompile();
    }

    [Fact]
    public async Task TestNativeMethods()
    {
        this.nativeMethodsTxt = "NativeMethods.txt";
        await this.InvokeGeneratorAndCompile();
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

    private async Task InvokeGeneratorAndCompile([CallerMemberName] string testCase = "")
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

    private async Task InvokeGenerator(string testCase)
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

    private async Task CompileGeneratedFilesWithSourceGenerators(string[] generatedFiles)
    {
        this.Logger.WriteLine("Compiling generated files with source generators...");

        this.compilation = this.starterCompilations["net9.0"];

        // Create syntax trees from the generated files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (string filePath in generatedFiles)
        {
            string sourceCode = await File.ReadAllTextAsync(filePath);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            syntaxTrees.Add(syntaxTree);
        }

        // Also add the assembly attribute


        this.compilation = this.compilation.AddSyntaxTrees(syntaxTrees);

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
            var generatorDriver = driver.RunGeneratorsAndUpdateCompilation(this.compilation, out var newCompilation, out var diagnostics);
            this.compilation = (CSharpCompilation)newCompilation;

            // Log any diagnostics from source generation
            foreach (var diagnostic in diagnostics)
            {
                this.Logger.WriteLine($"Diagnostic: {diagnostic}");
            }

            Assert.Empty(diagnostics);
        }
        else
        {
            this.Logger.WriteLine("No source generators found in the analyzers list");
        }

        // Get compilation diagnostics
        var compilationDiagnostics = this.compilation.GetDiagnostics();

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
        var emitResult = this.compilation.Emit(stream);

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
