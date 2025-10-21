// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Windows.CsWin32.BuildTasks;

/// <summary>
/// Interface for executing command line tools (test hook).
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a tool with the given command line arguments.
    /// Should return the process exit code (0 indicates success).
    /// </summary>
    /// <param name="pathToTool">Path to the executable.</param>
    /// <param name="responseFileCommands">Content that would be written to the response file (if any).</param>
    /// <param name="commandLineCommands">Command line (excluding response file commands).</param>
    /// <returns>The exit code (0 for success).</returns>
    int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands);
}

/// <summary>
/// MSBuild task to invoke CsWin32 code generation via the command line tool.
/// </summary>
public class CsWin32CodeGeneratorTask : ToolTask
{
    private IToolExecutor? toolExecutor;
    private List<ITaskItem> generatedFiles = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CsWin32CodeGeneratorTask"/> class.
    /// </summary>
    public CsWin32CodeGeneratorTask()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsWin32CodeGeneratorTask"/> class.
    /// </summary>
    /// <param name="toolExecutor">DI for unit testing.</param>
    public CsWin32CodeGeneratorTask(IToolExecutor? toolExecutor)
    {
        this.toolExecutor = toolExecutor;
    }

    /// <summary>
    /// Gets or sets the path to the NativeMethods.txt file containing API names to generate.
    /// </summary>
    [Required]
    public required string[] NativeMethodsTxt { get; set; }

    /// <summary>
    /// Gets or sets the path to the NativeMethods.json file containing generation options.
    /// </summary>
    public string? NativeMethodsJson { get; set; }

    /// <summary>
    /// Gets or sets the semicolon-separated paths to Windows metadata files (.winmd).
    /// </summary>
    [Required]
    public required string[] MetadataPaths { get; set; }

    /// <summary>
    /// Gets or sets the semicolon-separated paths to documentation files.
    /// </summary>
    public string[]? DocPaths { get; set; }

    /// <summary>
    /// Gets or sets the semicolon-separated paths to app-local allowed libraries.
    /// </summary>
    public string[]? AppLocalAllowedLibraries { get; set; }

    /// <summary>
    /// Gets or sets the output directory where generated files will be written.
    /// </summary>
    [Required]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Gets or sets the target framework version (affects available features).
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// Gets or sets the target platform (e.g., x86, x64, AnyCPU).
    /// </summary>
    public string? Platform { get; set; } = "AnyCPU";

    /// <summary>
    /// Gets or sets additional references to be included in the compilation context.
    /// </summary>
    public ITaskItem[]? References { get; set; }

    /// <summary>
    /// Gets or sets the name of the assembly being generated for.
    /// </summary>
    public string? AssemblyName { get; set; }

    /// <summary>
    /// Gets or sets the path to the strong name key file (.snk) for signing.
    /// </summary>
    public string? KeyFile { get; set; }

    /// <summary>
    /// Gets or sets the C# language version (e.g., 10, 11, 12, 13, Latest, Preview).
    /// </summary>
    public string? LangVersion { get; set; }

    /// <summary>
    /// Gets the generated source files.
    /// </summary>
    [Output]
    public ITaskItem[] GeneratedFiles => this.generatedFiles.ToArray();

    /// <summary>
    /// Gets or sets the path to the generator tool.
    /// </summary>
    [Required]
    public required string GeneratorToolPath { get; set; }

    /// <summary>
    /// Gets a value indicating whether the tool execution succeeded.
    /// </summary>
    [Output]
    public bool Succeeded { get; private set; }

    /// <inheritdoc />
    protected override string ToolName => "dotnet";

    /// <summary>
    /// Exposes the generated command line arguments (for tests).
    /// </summary>
    /// <returns>Command line arguments.</returns>
    public string GetCommandLineArguments() => this.GenerateCommandLineCommands() + " " + this.GenerateResponseFileCommands();

    /// <inheritdoc />
    protected override string GenerateFullPathToTool()
    {
        return this.ToolExe;
    }

    /// <inheritdoc />
    protected override string GenerateCommandLineCommands()
    {
        return this.GeneratorToolPath;
    }

    /// <inheritdoc />
    protected override string GenerateResponseFileCommands()
    {
        var commandLine = new CommandLineBuilder();

        // Required parameters
        commandLine.AppendSwitch("--native-methods-txt ");
        foreach (string nativeMethodsTxt in this.NativeMethodsTxt)
        {
            commandLine.AppendFileNameIfNotNull(nativeMethodsTxt);
        }

        commandLine.AppendSwitchIfNotNull("--output-path ", this.OutputPath);

        if (this.MetadataPaths?.Length > 0)
        {
            commandLine.AppendSwitch("--metadata-paths");
            foreach (string path in this.MetadataPaths)
            {
                commandLine.AppendFileNameIfNotNull(path.Trim());
            }
        }

        // Optional parameters
        commandLine.AppendSwitchIfNotNull("--native-methods-json ", this.NativeMethodsJson);

        if (this.DocPaths?.Length > 0)
        {
            commandLine.AppendSwitch("--doc-paths");
            foreach (string path in this.DocPaths)
            {
                commandLine.AppendFileNameIfNotNull(path.Trim());
            }
        }

        if (this.AppLocalAllowedLibraries?.Length > 0)
        {
            commandLine.AppendSwitch("--app-local-allowed-libraries");
            foreach (string path in this.AppLocalAllowedLibraries)
            {
                commandLine.AppendFileNameIfNotNull(path.Trim());
            }
        }

        commandLine.AppendSwitchIfNotNull("--target-framework ", this.TargetFramework);
        commandLine.AppendSwitchIfNotNull("--platform ", this.Platform);
        commandLine.AppendSwitchIfNotNull("--assembly-name ", this.AssemblyName);
        commandLine.AppendSwitchIfNotNull("--key-file ", this.KeyFile);
        commandLine.AppendSwitchIfNotNull("--language-version ", this.LangVersion);
        commandLine.AppendSwitch("--verbose ");

        if (this.References?.Length > 0)
        {
            commandLine.AppendSwitch("--references");
            foreach (ITaskItem reference in this.References)
            {
                commandLine.AppendFileNameIfNotNull(reference.ItemSpec);
            }
        }

        return commandLine.ToString();
    }

    /// <inheritdoc />
    protected override bool ValidateParameters()
    {
        if (this.NativeMethodsTxt.Length == 0)
        {
            this.Log.LogError($"{nameof(this.NativeMethodsTxt)} property must be specified.");
            return false;
        }

        if (this.MetadataPaths.Length == 0)
        {
            this.Log.LogError($"{nameof(this.MetadataPaths)} property must be specified.");
            return false;
        }

        if (string.IsNullOrEmpty(this.OutputPath))
        {
            this.Log.LogError($"{nameof(this.OutputPath)} property must be specified.");
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    protected override int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands)
    {
        if (this.toolExecutor is object)
        {
            return this.toolExecutor.ExecuteTool(pathToTool, responseFileCommands, commandLineCommands);
        }

        return base.ExecuteTool(pathToTool, responseFileCommands, commandLineCommands);
    }

    /// <inheritdoc/>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        base.LogEventsFromTextOutput(singleLine, messageImportance);

        // If this output is telling us about a generated file, record it.
        if (singleLine.StartsWith("Generated: "))
        {
            string generatedFile = singleLine.Replace("Generated: ", string.Empty).Trim();
            TaskItem generatedFileTaskItem = new(generatedFile);
            generatedFileTaskItem.SetMetadata("Generator", "CsWin32");
            this.generatedFiles.Add(generatedFileTaskItem);
        }
    }
}
