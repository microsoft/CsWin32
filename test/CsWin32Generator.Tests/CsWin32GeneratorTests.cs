// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsWin32Generator.Tests;

public class CsWin32GeneratorTests
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
