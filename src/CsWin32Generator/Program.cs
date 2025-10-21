// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Windows.CsWin32;

namespace CsWin32Generator;

/// <summary>
/// Main program for the CsWin32 command line code generator.
/// </summary>
public partial class Program
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private bool verbose;
    private string? assemblyName;
    private FileInfo? assemblyOriginatorKeyFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="Program"/> class.
    /// </summary>
    /// <param name="output">output.</param>
    /// <param name="error">error.</param>
    public Program(TextWriter output, TextWriter error)
    {
        this.output = output;
        this.error = error;
    }

    /// <summary>
    /// Entry point for the command line application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code (0 for success, 1 for failure).</returns>
    public static async Task<int> Main(string[] args)
    {
        return await new Program(Console.Out, Console.Error).Main(args);
    }

    /// <summary>
    /// Test entry point.
    /// </summary>
    /// <param name="args">args.</param>
    /// <param name="fullGeneration">fullGeneration.</param>
    /// <returns>exit code.</returns>
    public async Task<int> Main(
        string[] args,
        bool fullGeneration = false)
    {
        var nativeMethodsTxtOption = new Option<FileInfo[]>("--native-methods-txt")
        {
            Description = "Path to the NativeMethods.txt file(s) containing API names to generate.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var nativeMethodsJsonOption = new Option<FileInfo?>("--native-methods-json")
        {
            Description = "Path to the NativeMethods.json file containing generation options.",
        };

        var metadataPathsOption = new Option<FileInfo[]>("--metadata-paths")
        {
            Description = "Paths to Windows metadata files (.winmd).",
            AllowMultipleArgumentsPerToken = true,
        };

        var docPathsOption = new Option<FileInfo[]?>("--doc-paths")
        {
            Description = "Paths to documentation files.",
            AllowMultipleArgumentsPerToken = true,
        };

        var appLocalAllowedLibrariesOption = new Option<FileInfo[]?>("--app-local-allowed-libraries")
        {
            Description = "Paths to app-local allowed libraries.",
            AllowMultipleArgumentsPerToken = true,
        };

        var outputPathOption = new Option<DirectoryInfo>("--output-path")
        {
            Description = "Output directory where generated files will be written.",
            Required = true,
        };

        var targetFrameworkOption = new Option<string?>("--target-framework")
        {
            Description = "Target framework version (affects available features).",
        };

        var platformOption = new Option<string>("--platform")
        {
            Description = "Target platform (e.g., x86, x64, AnyCPU).",
            DefaultValueFactory = (x) => "AnyCPU",
        };

        var referencesOption = new Option<FileInfo[]?>("--references")
        {
            Description = "Additional references to be included in the compilation context.",
            AllowMultipleArgumentsPerToken = true,
        };

        var assemblyNameOption = new Option<string?>("--assembly-name")
        {
            Description = "The name of the assembly being generated for.",
        };

        var keyFileOption = new Option<FileInfo?>("--key-file")
        {
            Description = "Path to the strong name key file (.snk) for signing.",
        };

        var verboseOption = new Option<bool>("--verbose");

        var rootCommand = new RootCommand("CsWin32 Code Generator - Generates P/Invoke methods and supporting types from Windows metadata.")
        {
            nativeMethodsTxtOption,
            nativeMethodsJsonOption,
            metadataPathsOption,
            docPathsOption,
            appLocalAllowedLibrariesOption,
            outputPathOption,
            targetFrameworkOption,
            platformOption,
            referencesOption,
            assemblyNameOption,
            keyFileOption,
            verboseOption,
        };

        ParseResult parseResult = rootCommand.Parse(args);
        var nativeMethodsTxtFiles = parseResult.GetValue(nativeMethodsTxtOption)!;
        var nativeMethodsJson = parseResult.GetValue(nativeMethodsJsonOption);
        var metadataPaths = parseResult.GetValue(metadataPathsOption)!;
        var docPaths = parseResult.GetValue(docPathsOption);
        var appLocalAllowedLibraries = parseResult.GetValue(appLocalAllowedLibrariesOption);
        var outputPath = parseResult.GetValue(outputPathOption)!;
        var targetFramework = parseResult.GetValue(targetFrameworkOption);
        var platform = parseResult.GetValue(platformOption);
        var references = parseResult.GetValue(referencesOption);
        this.assemblyName = parseResult.GetValue(assemblyNameOption);
        this.assemblyOriginatorKeyFile = parseResult.GetValue(keyFileOption);
        this.verbose = parseResult.GetValue(verboseOption);

        // Check for errors before continuing.
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors)
            {
                this.ReportError($"{error.Message}");
            }

            return 1;
        }

        try
        {
            var result = await this.GenerateCode(
                nativeMethodsTxtFiles,
                nativeMethodsJson,
                metadataPaths,
                docPaths,
                appLocalAllowedLibraries,
                outputPath,
                targetFramework,
                platform ?? "AnyCPU",
                references,
                fullGeneration);

            return result ? 0 : 1;
        }
        catch (Exception ex)
        {
            this.ReportError($"{ex.Message}");
            if (ex.InnerException != null)
            {
                this.ReportError($"Inner exception: {ex.InnerException.Message}");
            }

            return 1;
        }
    }

    /// <summary>
    /// Generates code using the CsWin32 generator.
    /// </summary>
    /// <param name="nativeMethodsTxtFiles">Path to the NativeMethods.txt file.</param>
    /// <param name="nativeMethodsJson">Path to the NativeMethods.json file (optional).</param>
    /// <param name="metadataPaths">Paths to Windows metadata files.</param>
    /// <param name="docPaths">Paths to documentation files (optional).</param>
    /// <param name="appLocalAllowedLibraries">Paths to app-local allowed libraries (optional).</param>
    /// <param name="outputPath">Output directory for generated files.</param>
    /// <param name="targetFramework">Target framework version.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="references">Additional assembly references (optional).</param>
    /// <param name="fullGeneration">Whether to generate the full set of APIs.</param>
    /// <returns>True if successful, false otherwise.</returns>
    private Task<bool> GenerateCode(
        FileInfo[] nativeMethodsTxtFiles,
        FileInfo? nativeMethodsJson,
        FileInfo[] metadataPaths,
        FileInfo[]? docPaths,
        FileInfo[]? appLocalAllowedLibraries,
        DirectoryInfo outputPath,
        string? targetFramework,
        string platform,
        FileInfo[]? references,
        bool fullGeneration)
    {
        this.VerboseWriteLine("Starting CsWin32 code generation...");

        foreach (var nativeMethodsTxtFile in nativeMethodsTxtFiles)
        {
            if (!nativeMethodsTxtFile.Exists)
            {
                this.ReportError($"NativeMethods.txt file not found: {nativeMethodsTxtFile.FullName}");
                return Task.FromResult(false);
            }
        }

        if (nativeMethodsJson is object && !nativeMethodsJson.Exists)
        {
            this.ReportError($"NativeMethods.json file not found: {nativeMethodsJson.FullName}");
            return Task.FromResult(false);
        }

        if (metadataPaths.Length == 0)
        {
            this.ReportError("At least one metadata path must be provided.");
            return Task.FromResult(false);
        }

        // Load generator options from NativeMethods.json if provided
        GeneratorOptions options = this.LoadGeneratorOptions(nativeMethodsJson);

        // If unspecified, default to using other source generators.
        if (!options.ComInterop.UseComSourceGenerators.HasValue && targetFramework != "net472")
        {
            options.ComInterop.UseComSourceGenerators = true;
        }

        this.VerboseWriteLine($"Loaded generator options. AllowMarshaling: {options.AllowMarshaling}, ClassName: {options.ClassName}");

        // Validate metadata files exist
        foreach (var metadataPath in metadataPaths)
        {
            if (!metadataPath.Exists)
            {
                this.ReportError($"Metadata file not found: {metadataPath.FullName}");
                return Task.FromResult(false);
            }
        }

        // Create compilation context
        CSharpCompilation? compilation = this.CreateCompilation(allowUnsafeBlocks: true, platform, references);
        CSharpParseOptions? parseOptions = this.CreateParseOptions(targetFramework);
        this.VerboseWriteLine($"Created compilation context with platform: {platform}, language version: {parseOptions?.LanguageVersion}");

        // Load docs if available
        Docs? docs = this.LoadDocs(docPaths);
        if (docs != null)
        {
            this.VerboseWriteLine("Loaded API documentation.");
        }

        // Process app-local libraries
        IEnumerable<string> appLocalLibrariesNames = appLocalAllowedLibraries?.Select(f => f.Name) ?? Array.Empty<string>();

        // Create generators for all metadata paths
        var generators = new List<Generator>();
        foreach (var metadataPath in metadataPaths)
        {
            this.VerboseWriteLine($"Creating generator for: {metadataPath.Name}");
            generators.Add(new Generator(metadataPath.FullName, docs, appLocalLibrariesNames, options, compilation, parseOptions));
        }

        // Create super generator
        using SuperGenerator superGenerator = generators.Count == 1
            ? SuperGenerator.Combine(generators[0])
            : SuperGenerator.Combine(generators.ToArray());

        this.VerboseWriteLine($"Created super generator with {generators.Count} generator(s).");

        List<(FileInfo, string[])> nativeMethodTxts = new();
        foreach (var nativeMethodsTxtFile in nativeMethodsTxtFiles)
        {
            var lines = File.ReadAllLines(nativeMethodsTxtFile.FullName);
            nativeMethodTxts.Add((nativeMethodsTxtFile, lines));

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("-", StringComparison.Ordinal))
                {
                    superGenerator.AddGeneratorExclusion(trimmedLine[1..]);
                }
            }
        }

        foreach (var (nativeMethodsTxtFile, lines) in nativeMethodTxts)
        {
            // Process NativeMethods.txt file
            if (!this.ProcessNativeMethodsFile(superGenerator, nativeMethodsTxtFile, lines))
            {
                return Task.FromResult(false);
            }
        }

        if (fullGeneration)
        {
            superGenerator.GenerateAll(CancellationToken.None);
        }

        // Generate compilation units and write to files
        if (!this.GenerateAndWriteFiles(superGenerator, outputPath))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Loads generator options from the NativeMethods.json file.
    /// </summary>
    /// <param name="nativeMethodsJson">Path to the NativeMethods.json file (optional).</param>
    /// <returns>Generator options instance.</returns>
    private GeneratorOptions LoadGeneratorOptions(FileInfo? nativeMethodsJson)
    {
        if (nativeMethodsJson is object)
        {
            string optionsJson = File.ReadAllText(nativeMethodsJson.FullName);
            return JsonSerializer.Deserialize(optionsJson, GeneratorOptionsSerializerContext.Default.GeneratorOptions) ?? new GeneratorOptions();
        }

        return new();
    }

    /// <summary>
    /// Creates a C# compilation context for code generation.
    /// </summary>
    /// <param name="allowUnsafeBlocks">Whether unsafe code is allowed.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="references">Additional assembly references (optional).</param>
    /// <returns>C# compilation instance or null if creation fails.</returns>
    private CSharpCompilation? CreateCompilation(bool allowUnsafeBlocks, string platform, FileInfo[]? references)
    {
        var metadataReferences = new List<MetadataReference>();

        // Add additional references if provided
        if (references is object)
        {
            foreach (var reference in references)
            {
                if (reference.Exists)
                {
                    metadataReferences.Add(MetadataReference.CreateFromFile(reference.FullName));
                }
                else
                {
                    this.ReportError($"Reference path not found {reference.FullName}");
                }
            }
        }

        Platform compilationPlatform = Platform.AnyCpu;
        if (platform.Equals("x86", StringComparison.OrdinalIgnoreCase))
        {
            compilationPlatform = Platform.X86;
        }
        else if (platform.Equals("x64", StringComparison.OrdinalIgnoreCase))
        {
            compilationPlatform = Platform.X64;
        }
        else if (platform.Equals("arm64", StringComparison.OrdinalIgnoreCase))
        {
            compilationPlatform = Platform.Arm64;
        }
        else if (platform.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase))
        {
            compilationPlatform = Platform.AnyCpu;
        }

        var compilationOptions = new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafeBlocks,
            platform: compilationPlatform);

        // Handle strong name signing if a key file is provided
        if (this.assemblyOriginatorKeyFile is not null)
        {
            if (!this.assemblyOriginatorKeyFile.Exists)
            {
                this.ReportError($"Key file not found: {this.assemblyOriginatorKeyFile.FullName}");
                return null;
            }

            compilationOptions = compilationOptions
                .WithStrongNameProvider(new DesktopStrongNameProvider())
                .WithCryptoKeyFile(this.assemblyOriginatorKeyFile.FullName);

            this.VerboseWriteLine("Compilation configured for strong-name signing");
        }

        return CSharpCompilation.Create(
            assemblyName: this.assemblyName ?? "GeneratedCode",
            syntaxTrees: null,
            references: metadataReferences,
            options: compilationOptions);
    }

    /// <summary>
    /// Creates C# parse options based on the target framework.
    /// </summary>
    /// <param name="targetFramework">Target framework version (optional).</param>
    /// <returns>C# parse options instance or null if creation fails.</returns>
    private CSharpParseOptions? CreateParseOptions(string? targetFramework)
    {
        return new CSharpParseOptions(languageVersion: LanguageVersion.CSharp13);
    }

    /// <summary>
    /// Loads documentation from the specified paths.
    /// </summary>
    /// <param name="docPaths">Paths to documentation files (optional).</param>
    /// <returns>Merged documentation instance or null if none available.</returns>
    private Docs? LoadDocs(FileInfo[]? docPaths)
    {
        if (docPaths?.Length > 0)
        {
            var docsList = new List<Docs>();
            foreach (var docPath in docPaths)
            {
                try
                {
                    if (docPath.Exists)
                    {
                        docsList.Add(Docs.Get(docPath.FullName));
                    }
                }
                catch (Exception ex)
                {
                    this.ReportWarning($"Failed to load documentation from {docPath.FullName}: {ex.Message}");
                }
            }

            return docsList.Count > 0 ? Docs.Merge(docsList) : null;
        }

        return null;
    }

    /// <summary>
    /// Processes the NativeMethods.txt file to generate APIs.
    /// </summary>
    /// <param name="superGenerator">The super generator instance.</param>
    /// <param name="nativeMethodsTxt">Path to the NativeMethods.txt file.</param>
    /// <param name="lines">Lines from the NativeMethods.txt file.</param>
    /// <returns>True if processing succeeded, false otherwise.</returns>
    private unsafe bool ProcessNativeMethodsFile(SuperGenerator superGenerator, FileInfo nativeMethodsTxt, string[] lines)
    {
        try
        {
            this.VerboseWriteLine($"Processing {lines.Length} lines from {nativeMethodsTxt.Name}");

            int processedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            foreach (string line in lines)
            {
                string name = line.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("//", StringComparison.Ordinal) || name.StartsWith("-", StringComparison.Ordinal))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    // Skip banned API check for now since it's not accessible
                    // TODO: Consider making Generator.GetBannedAPIs or BannedAPIs public if needed
                    if (name.EndsWith(".*", StringComparison.Ordinal))
                    {
                        string? moduleName = name.Substring(0, name.Length - 2);
                        int matches = superGenerator.TryGenerateAllExternMethods(moduleName, CancellationToken.None);
                        if (matches == 0)
                        {
                            this.ReportWarning($"No methods found under module '{moduleName}'");
                        }
                        else
                        {
                            this.VerboseWriteLine($"Generated {matches} methods from module '{moduleName}'");
                        }

                        processedCount++;
                        continue;
                    }

                    superGenerator.TryGenerate(name, out IReadOnlyCollection<string> matchingApis, out IReadOnlyCollection<string> redirectedEnums, CancellationToken.None);

                    foreach (string declaringEnum in redirectedEnums)
                    {
                        this.ReportWarning($"Using the name of the enum that declares this constant: {declaringEnum}");
                    }

                    switch (matchingApis.Count)
                    {
                        case 0:
                            this.ReportError($"Method, type or constant '{name}' not found");
                            errorCount++;
                            break;
                        case 1:
                            this.InfoWriteLine($"Generated: {name}");
                            processedCount++;
                            break;
                        case > 1:
                            this.ReportError($"The API '{name}' is ambiguous. Please specify one of: {string.Join(", ", matchingApis.Select(api => $"\"{api}\""))}");
                            errorCount++;
                            break;
                    }
                }
                catch (PlatformIncompatibleException)
                {
                    this.ReportError($"API '{name}' is not available for the target platform");
                    errorCount++;
                }
                catch (Exception ex)
                {
                    this.ReportError($"'{name}': {ex.Message}");
                    errorCount++;
                }
            }

            this.VerboseWriteLine($"Processing complete. Processed: {processedCount}, Skipped: {skippedCount}, Errors: {errorCount}");
            return errorCount == 0;
        }
        catch (Exception ex)
        {
            this.ReportError($"Failed to process NativeMethods.txt file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates and writes source files to the output directory.
    /// </summary>
    /// <param name="superGenerator">The super generator instance.</param>
    /// <param name="outputPath">Output directory for generated files.</param>
    /// <returns>True if generation succeeded, false otherwise.</returns>
    private bool GenerateAndWriteFiles(SuperGenerator superGenerator, DirectoryInfo outputPath)
    {
        try
        {
            // Ensure output directory exists
            outputPath.Create();
            this.VerboseWriteLine($"Output directory: {outputPath.FullName}");

            List<string> outputFiles = new();

            var compilationUnits = superGenerator.GetCompilationUnits(CancellationToken.None);
            int fileCount = 0;

            foreach (KeyValuePair<string, CompilationUnitSyntax> unit in compilationUnits)
            {
                string fileName = unit.Key;
                string filePath = Path.Combine(outputPath.FullName, fileName);

                // Write the file
                string sourceText = unit.Value.GetText(Encoding.UTF8).ToString();
                File.WriteAllText(filePath, sourceText, Encoding.UTF8);

                outputFiles.Add(filePath);

                this.InfoWriteLine($"Generated: {fileName}");
                fileCount++;
            }

            var generatedFilesTxt = Path.Combine(outputPath.FullName, "CsWin32GeneratedFiles.txt");

            File.WriteAllLines(generatedFilesTxt, outputFiles);

            this.InfoWriteLine($"Generated: {generatedFilesTxt}");

            this.VerboseWriteLine($"Successfully generated {fileCount} source files.");
            return true;
        }
        catch (Exception ex)
        {
            this.VerboseWriteLine($"Failed to generate and write files: {ex.Message}");
            return false;
        }
    }

    private void ReportError(string message)
    {
        this.error.WriteLine($"CsWin32 : error : {message}");
    }

    private void ReportWarning(string message)
    {
        this.output.WriteLine($"CsWin32 : warning : {message}");
    }

    private void InfoWriteLine(string message)
    {
        this.output.WriteLine(message);
    }

    private void VerboseWriteLine(string message)
    {
        if (this.verbose)
        {
            this.output.WriteLine(message);
        }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(GeneratorOptions))]
    internal partial class GeneratorOptionsSerializerContext : JsonSerializerContext
    {
    }
}
