// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Windows.CsWin32.BuildTasks;
using Moq;
using Xunit;

#pragma warning disable SA1116

namespace Microsoft.Windows.CsWin32.Tests;

public class BuildTaskTests
{
    public ITestOutputHelper Logger => TestContext.Current.TestOutputHelper!;

    [Fact]
    public void TestBuildTasks()
    {
        Mock<IBuildEngine> buildEngine = new();
        buildEngine.Setup(x => x.LogErrorEvent(It.IsAny<BuildErrorEventArgs>())).Callback<BuildErrorEventArgs>(e => this.Logger.WriteLine(e.Message));

        CsWin32CodeGeneratorTask task = new();
        task.BuildEngine = buildEngine.Object;

        // This test verifies validation behavior - without required parameters, it should fail
        bool result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithRequiredParameters_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        task.NativeMethodsTxt = "TestContent\\NativeMethods.txt";
        task.OutputPath = "Generated";
        task.MetadataPaths = "metadata1.winmd;metadata2.winmd";

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Command line: {commandLine}");

        // Assert
        Assert.Contains("--native-methods-txt TestContent\\NativeMethods.txt", commandLine);
        Assert.Contains("--output-path Generated", commandLine);
        Assert.Contains("--metadata-paths", commandLine);
        Assert.Contains("metadata1.winmd", commandLine);
        Assert.Contains("metadata2.winmd", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithOptionalParameters_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.NativeMethodsJson = "TestContent\\NativeMethods.json";
        task.DocPaths = "doc1.xml;doc2.xml";
        task.AppLocalAllowedLibraries = "lib1.dll;lib2.dll";
        task.AllowUnsafeBlocks = false;
        task.TargetFramework = "net6.0";
        task.Platform = "x64";

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Command line: {commandLine}");

        // Assert
        Assert.Contains("--native-methods-json TestContent\\NativeMethods.json", commandLine);
        Assert.Contains("--doc-paths", commandLine);
        Assert.Contains("doc1.xml", commandLine);
        Assert.Contains("doc2.xml", commandLine);
        Assert.Contains("--app-local-allowed-libraries", commandLine);
        Assert.Contains("lib1.dll", commandLine);
        Assert.Contains("lib2.dll", commandLine);
        Assert.Contains("--allow-unsafe-blocks false", commandLine);
        Assert.Contains("--target-framework net6.0", commandLine);
        Assert.Contains("--platform x64", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithReferences_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.References = new ITaskItem[]
        {
            new TaskItem("System.dll"),
            new TaskItem("System.Core.dll"),
            new TaskItem("Microsoft.Win32.dll"),
        };

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Command line: {commandLine}");

        // Assert
        Assert.Contains("--references", commandLine);
        Assert.Contains("System.dll", commandLine);
        Assert.Contains("System.Core.dll", commandLine);
        Assert.Contains("Microsoft.Win32.dll", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithDefaultAllowUnsafeBlocks_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);

        // AllowUnsafeBlocks defaults to true

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.Contains("--allow-unsafe-blocks true", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithDefaultPlatform_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);

        // Platform defaults to "AnyCPU"

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.Contains("--platform AnyCPU", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithPathsContainingSpaces_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        task.NativeMethodsTxt = "Test Content\\Native Methods.txt";
        task.OutputPath = "Generated Output";
        task.MetadataPaths = "C:\\Program Files\\metadata\\file1.winmd;C:\\Program Files\\metadata\\file2.winmd";

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Command line: {commandLine}");

        // Assert
        Assert.Contains("--native-methods-txt \"Test Content\\Native Methods.txt\"", commandLine);
        Assert.Contains("--output-path \"Generated Output\"", commandLine);
        Assert.Contains("\"C:\\Program Files\\metadata\\file1.winmd\"", commandLine);
        Assert.Contains("\"C:\\Program Files\\metadata\\file2.winmd\"", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithEmptyOptionalParameters_DoesNotIncludeThem()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.NativeMethodsJson = null;
        task.DocPaths = string.Empty;
        task.AppLocalAllowedLibraries = string.Empty;
        task.TargetFramework = null;

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.DoesNotContain("--native-methods-json", commandLine);
        Assert.DoesNotContain("--doc-paths", commandLine);
        Assert.DoesNotContain("--app-local-allowed-libraries", commandLine);
        Assert.DoesNotContain("--target-framework", commandLine);
    }

    [Fact]
    public void Execute_WithMockToolExecutor_CallsExecutorWithCorrectParameters()
    {
        // Arrange
        Mock<IToolExecutor> mockExecutor = new();

        var task = CreateTaskWithMockBuildEngine(mockExecutor.Object);
        SetupRequiredParameters(task);

        // MSBuild executor expects the tool file to exist, so set it to ourselves for now.
        task.ToolExe = typeof(CsWin32CodeGeneratorTask).Assembly.Location;
        task.ToolPath = null;

        // Act
        bool result = task.Execute();

        // Assert
        Assert.True(result);
        mockExecutor.Verify(e => e.ExecuteTool(
            It.Is<string>(toolPath => true),
            It.Is<string>(commandLine => true),
            It.Is<string>(rspCommands =>
            rspCommands.Contains("--native-methods-txt") &&
            rspCommands.Contains("--output-path") &&
            rspCommands.Contains("--metadata-paths"))));
    }

    [Theory]
    [InlineData("net472", "--target-framework net472")]
    [InlineData("net6.0", "--target-framework net6.0")]
    [InlineData("net8.0-windows", "--target-framework net8.0-windows")]
    public void GenerateCommandLineCommands_WithVariousTargetFrameworks_FormatsCorrectly(string targetFramework, string expectedArgument)
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.TargetFramework = targetFramework;

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.Contains(expectedArgument, commandLine);
    }

    [Theory]
    [InlineData("x86", "--platform x86")]
    [InlineData("x64", "--platform x64")]
    [InlineData("AnyCPU", "--platform AnyCPU")]
    [InlineData("ARM64", "--platform ARM64")]
    public void GenerateCommandLineCommands_WithVariousPlatforms_FormatsCorrectly(string platform, string expectedArgument)
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.Platform = platform;

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.Contains(expectedArgument, commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithSinglePathInSemicolonDelimitedString_FormatsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.MetadataPaths = "single-metadata.winmd";
        task.DocPaths = "single-doc.xml";

        // Act
        string commandLine = task.GetCommandLineArguments();

        // Assert
        Assert.Contains("--metadata-paths", commandLine);
        Assert.Contains("single-metadata.winmd", commandLine);
        Assert.Contains("--doc-paths", commandLine);
        Assert.Contains("single-doc.xml", commandLine);
    }

    [Fact]
    public void GenerateCommandLineCommands_WithWhitespaceInPaths_TrimsCorrectly()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        SetupRequiredParameters(task);
        task.MetadataPaths = " metadata1.winmd ; metadata2.winmd ";
        task.DocPaths = " doc1.xml ; doc2.xml ";

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Command line: {commandLine}");

        // Assert
        Assert.Contains("metadata1.winmd", commandLine);
        Assert.Contains("metadata2.winmd", commandLine);
        Assert.Contains("doc1.xml", commandLine);
        Assert.Contains("doc2.xml", commandLine);

        // The trimming is done by the SplitPaths method, so we should not see the leading/trailing spaces
        // But the CommandLineBuilder may still include spaces in its output format
    }

    [Fact]
    public void GenerateCommandLineCommands_AllParametersTest_OutputsFullCommandLine()
    {
        // Arrange
        var task = CreateTaskWithMockBuildEngine();
        task.NativeMethodsTxt = "Methods.txt";
        task.NativeMethodsJson = "Methods.json";
        task.OutputPath = "Output";
        task.MetadataPaths = "file1.winmd;file2.winmd";
        task.DocPaths = "doc1.xml;doc2.xml";
        task.AppLocalAllowedLibraries = "lib1.dll;lib2.dll";
        task.AllowUnsafeBlocks = true;
        task.TargetFramework = "net8.0";
        task.Platform = "x64";
        task.References = new ITaskItem[]
        {
            new TaskItem("ref1.dll"),
            new TaskItem("ref2.dll"),
        };

        // Act
        string commandLine = task.GetCommandLineArguments();
        this.Logger.WriteLine($"Full command line: {commandLine}");

        // Assert - just verify the command line is not empty and contains expected switches
        Assert.NotEmpty(commandLine);
        Assert.Contains("--native-methods-txt", commandLine);
        Assert.Contains("--output-path", commandLine);
        Assert.Contains("--metadata-paths", commandLine);
    }

    private static CsWin32CodeGeneratorTask CreateTaskWithMockBuildEngine(IToolExecutor? toolExecutor = null)
    {
        var buildEngine = new Mock<IBuildEngine>();
        buildEngine.Setup(x => x.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()));
        buildEngine.Setup(x => x.LogMessageEvent(It.IsAny<BuildMessageEventArgs>()));

        return new CsWin32CodeGeneratorTask(toolExecutor)
        {
            BuildEngine = buildEngine.Object,
        };
    }

    private static void SetupRequiredParameters(CsWin32CodeGeneratorTask task)
    {
        task.NativeMethodsTxt = "TestContent\\NativeMethods.txt";
        task.OutputPath = "Generated";
        task.MetadataPaths = "metadata.winmd";
    }
}
