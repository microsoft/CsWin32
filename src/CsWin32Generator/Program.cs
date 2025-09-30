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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly char[] ZeroWhiteSpace = new char[]
    {
        '\uFEFF', // ZERO WIDTH NO-BREAK SPACE (U+FEFF)
        '\u200B', // ZERO WIDTH SPACE (U+200B)
    };

    /// <summary>
    /// Gets or sets a value indicating whether to generate the full set of APIs, including those not explicitly requested.
    /// </summary>
    public static bool FullGeneration { get; set; }

    /// <summary>
    /// Entry point for the command line application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code (0 for success, 1 for failure).</returns>
    public static async Task<int> Main(string[] args)
    {
        var nativeMethodsTxtOption = new Option<FileInfo>(
            name: "--native-methods-txt",
            description: "Path to the NativeMethods.txt file containing API names to generate.")
        {
            IsRequired = true,
        };

        var nativeMethodsJsonOption = new Option<FileInfo?>(
            name: "--native-methods-json",
            description: "Path to the NativeMethods.json file containing generation options.");

        var metadataPathsOption = new Option<FileInfo[]>(
            name: "--metadata-paths",
            description: "Paths to Windows metadata files (.winmd).")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var docPathsOption = new Option<FileInfo[]?>(
            name: "--doc-paths",
            description: "Paths to documentation files.")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var appLocalAllowedLibrariesOption = new Option<FileInfo[]?>(
            name: "--app-local-allowed-libraries",
            description: "Paths to app-local allowed libraries.")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var outputPathOption = new Option<DirectoryInfo>(
            name: "--output-path",
            description: "Output directory where generated files will be written.")
        {
            IsRequired = true,
        };

        var allowUnsafeBlocksOption = new Option<bool>(
            name: "--allow-unsafe-blocks",
            getDefaultValue: () => true,
            description: "Whether unsafe code is allowed.");

        var targetFrameworkOption = new Option<string?>(
            name: "--target-framework",
            description: "Target framework version (affects available features).");

        var platformOption = new Option<string>(
            name: "--platform",
            getDefaultValue: () => "AnyCPU",
            description: "Target platform (e.g., x86, x64, AnyCPU).");

        var referencesOption = new Option<FileInfo[]?>(
            name: "--references",
            description: "Additional references to be included in the compilation context.")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var rootCommand = new RootCommand("CsWin32 Code Generator - Generates P/Invoke methods and supporting types from Windows metadata.")
        {
            nativeMethodsTxtOption,
            nativeMethodsJsonOption,
            metadataPathsOption,
            docPathsOption,
            appLocalAllowedLibrariesOption,
            outputPathOption,
            allowUnsafeBlocksOption,
            targetFrameworkOption,
            platformOption,
            referencesOption,
        };

        ParseResult parseResult = rootCommand.Parse(args);
        var nativeMethodsTxt = parseResult.GetValueForOption(nativeMethodsTxtOption)!;
        var nativeMethodsJson = parseResult.GetValueForOption(nativeMethodsJsonOption);
        var metadataPaths = parseResult.GetValueForOption(metadataPathsOption)!;
        var docPaths = parseResult.GetValueForOption(docPathsOption);
        var appLocalAllowedLibraries = parseResult.GetValueForOption(appLocalAllowedLibrariesOption);
        var outputPath = parseResult.GetValueForOption(outputPathOption)!;
        var allowUnsafeBlocks = parseResult.GetValueForOption(allowUnsafeBlocksOption);
        var targetFramework = parseResult.GetValueForOption(targetFrameworkOption);
        var platform = parseResult.GetValueForOption(platformOption);
        var references = parseResult.GetValueForOption(referencesOption);

        // Check for errors before continuing.
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors)
            {
                Console.Error.WriteLine($"cswin32 : error : {error.Message}");
            }

            return 1;
        }

        try
        {
            var result = await GenerateCode(
                nativeMethodsTxt,
                nativeMethodsJson,
                metadataPaths,
                docPaths,
                appLocalAllowedLibraries,
                outputPath,
                allowUnsafeBlocks,
                targetFramework,
                platform ?? "AnyCPU", // Provide default value for platform
                references);

            return result ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CsWin32 : error : {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"CsWin32 : error : Inner exception: {ex.InnerException.Message}");
            }

            return 1;
        }
    }

    /// <summary>
    /// Generates code using the CsWin32 generator.
    /// </summary>
    /// <param name="nativeMethodsTxt">Path to the NativeMethods.txt file.</param>
    /// <param name="nativeMethodsJson">Path to the NativeMethods.json file (optional).</param>
    /// <param name="metadataPaths">Paths to Windows metadata files.</param>
    /// <param name="docPaths">Paths to documentation files (optional).</param>
    /// <param name="appLocalAllowedLibraries">Paths to app-local allowed libraries (optional).</param>
    /// <param name="outputPath">Output directory for generated files.</param>
    /// <param name="allowUnsafeBlocks">Whether unsafe code is allowed.</param>
    /// <param name="targetFramework">Target framework version.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="references">Additional assembly references (optional).</param>
    /// <returns>True if successful, false otherwise.</returns>
    private static Task<bool> GenerateCode(
        FileInfo nativeMethodsTxt,
        FileInfo? nativeMethodsJson,
        FileInfo[] metadataPaths,
        FileInfo[]? docPaths,
        FileInfo[]? appLocalAllowedLibraries,
        DirectoryInfo outputPath,
        bool allowUnsafeBlocks,
        string? targetFramework,
        string platform,
        FileInfo[]? references)
    {
        Console.WriteLine("Starting CsWin32 code generation...");

        if (!nativeMethodsTxt.Exists)
        {
            Console.Error.WriteLine($"NativeMethods.txt file not found: {nativeMethodsTxt.FullName}");
            return Task.FromResult(false);
        }

        if (metadataPaths.Length == 0)
        {
            Console.Error.WriteLine("At least one metadata path must be provided.");
            return Task.FromResult(false);
        }

        // Load generator options from NativeMethods.json if provided
        GeneratorOptions options = LoadGeneratorOptions(nativeMethodsJson);

        // If unspecified, default to using other source generators.
        if (!options.ComInterop.UseComSourceGenerators.HasValue && targetFramework != "net472")
        {
            options.ComInterop.UseComSourceGenerators = true;
        }

        Console.WriteLine($"Loaded generator options. AllowMarshaling: {options.AllowMarshaling}, ClassName: {options.ClassName}");

        // Validate metadata files exist
        foreach (var metadataPath in metadataPaths)
        {
            if (!metadataPath.Exists)
            {
                Console.Error.WriteLine($"Metadata file not found: {metadataPath.FullName}");
                return Task.FromResult(false);
            }
        }

        // Create compilation context
        CSharpCompilation? compilation = CreateCompilation(allowUnsafeBlocks, platform, references);
        CSharpParseOptions? parseOptions = CreateParseOptions(targetFramework);
        Console.WriteLine($"Created compilation context with platform: {platform}, language version: {parseOptions?.LanguageVersion}");

        // Load docs if available
        Docs? docs = LoadDocs(docPaths);
        if (docs != null)
        {
            Console.WriteLine("Loaded API documentation.");
        }

        // Process app-local libraries
        IEnumerable<string> appLocalLibrariesNames = appLocalAllowedLibraries?.Select(f => f.Name) ?? Array.Empty<string>();

        // Create generators for all metadata paths
        var generators = new List<Generator>();
        foreach (var metadataPath in metadataPaths)
        {
            Console.WriteLine($"Creating generator for: {metadataPath.Name}");
            generators.Add(new Generator(metadataPath.FullName, docs, appLocalLibrariesNames, options, compilation, parseOptions));
        }

        // Create super generator
        using SuperGenerator superGenerator = generators.Count == 1
            ? SuperGenerator.Combine(generators[0])
            : SuperGenerator.Combine(generators.ToArray());

        Console.WriteLine($"Created super generator with {generators.Count} generator(s).");

        // Process NativeMethods.txt file
        if (!ProcessNativeMethodsFile(superGenerator, nativeMethodsTxt))
        {
            return Task.FromResult(false);
        }

        if (FullGeneration)
        {
            superGenerator.GenerateAll(CancellationToken.None);
        }

        // Generate compilation units and write to files
        if (!GenerateAndWriteFiles(superGenerator, outputPath))
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
    private static GeneratorOptions LoadGeneratorOptions(FileInfo? nativeMethodsJson)
    {
        if (nativeMethodsJson?.Exists != true)
        {
            return new GeneratorOptions();
        }

        try
        {
            string optionsJson = File.ReadAllText(nativeMethodsJson.FullName);
            return JsonSerializer.Deserialize(optionsJson, GeneratorOptionsSerializerContext.Default.GeneratorOptions) ?? new GeneratorOptions();
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Failed to parse NativeMethods.json: {ex.Message}");
            return new GeneratorOptions();
        }
    }

    /// <summary>
    /// Creates a C# compilation context for code generation.
    /// </summary>
    /// <param name="allowUnsafeBlocks">Whether unsafe code is allowed.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="references">Additional assembly references (optional).</param>
    /// <returns>C# compilation instance or null if creation fails.</returns>
    private static CSharpCompilation? CreateCompilation(bool allowUnsafeBlocks, string platform, FileInfo[]? references)
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
            }
        }

        Microsoft.CodeAnalysis.Platform compilationPlatform = platform switch
        {
            "x86" => Microsoft.CodeAnalysis.Platform.X86,
            "x64" => Microsoft.CodeAnalysis.Platform.X64,
            "arm64" => Microsoft.CodeAnalysis.Platform.Arm64,
            _ => Microsoft.CodeAnalysis.Platform.AnyCpu,
        };

        var compilationOptions = new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafeBlocks,
            platform: compilationPlatform);

        return CSharpCompilation.Create(
            assemblyName: "GeneratedCode",
            syntaxTrees: null,
            references: metadataReferences,
            options: compilationOptions);
    }

    /// <summary>
    /// Creates C# parse options based on the target framework.
    /// </summary>
    /// <param name="targetFramework">Target framework version (optional).</param>
    /// <returns>C# parse options instance or null if creation fails.</returns>
    private static CSharpParseOptions? CreateParseOptions(string? targetFramework)
    {
        // Determine language version based on target framework
        LanguageVersion languageVersion = targetFramework switch
        {
            var tf when tf?.StartsWith("net9.0", StringComparison.Ordinal) == true => LanguageVersion.Latest,
            var tf when tf?.StartsWith("net8.0", StringComparison.Ordinal) == true => LanguageVersion.Latest,
            var tf when tf?.StartsWith("net7.0", StringComparison.Ordinal) == true => LanguageVersion.Latest,
            var tf when tf?.StartsWith("net6.0", StringComparison.Ordinal) == true => LanguageVersion.CSharp9,
            _ => LanguageVersion.CSharp9,
        };

        return new CSharpParseOptions(languageVersion: languageVersion);
    }

    /// <summary>
    /// Loads documentation from the specified paths.
    /// </summary>
    /// <param name="docPaths">Paths to documentation files (optional).</param>
    /// <returns>Merged documentation instance or null if none available.</returns>
    private static Docs? LoadDocs(FileInfo[]? docPaths)
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
                    Console.WriteLine($"Warning: Failed to load documentation from {docPath.FullName}: {ex.Message}");
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
    /// <returns>True if processing succeeded, false otherwise.</returns>
    private static bool ProcessNativeMethodsFile(SuperGenerator superGenerator, FileInfo nativeMethodsTxt)
    {
        try
        {
            var lines = File.ReadAllLines(nativeMethodsTxt.FullName);
            Console.WriteLine($"Processing {lines.Length} lines from {nativeMethodsTxt.Name}");

            int processedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            foreach (string line in lines)
            {
                string name = line.Trim().Trim(ZeroWhiteSpace);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("//", StringComparison.InvariantCulture))
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
                            Console.WriteLine($"Warning: No methods found under module '{moduleName}'");
                        }
                        else
                        {
                            Console.WriteLine($"Generated {matches} methods from module '{moduleName}'");
                        }

                        processedCount++;
                        continue;
                    }

                    superGenerator.TryGenerate(name, out IReadOnlyCollection<string> matchingApis, out IReadOnlyCollection<string> redirectedEnums, CancellationToken.None);

                    foreach (string declaringEnum in redirectedEnums)
                    {
                        Console.WriteLine($"Warning: Use the name of the enum that declares this constant: {declaringEnum}");
                    }

                    switch (matchingApis.Count)
                    {
                        case 0:
                            Console.WriteLine($"Warning: Method, type or constant '{name}' not found");
                            errorCount++;
                            break;
                        case 1:
                            Console.WriteLine($"Generated: {name}");
                            processedCount++;
                            break;
                        case > 1:
                            Console.Error.WriteLine($"Error: The API '{name}' is ambiguous. Please specify one of: {string.Join(", ", matchingApis.Select(api => $"\"{api}\""))}");
                            errorCount++;
                            break;
                    }
                }
                catch (PlatformIncompatibleException)
                {
                    Console.WriteLine($"Warning: API '{name}' is not available for the target platform");
                    skippedCount++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"CsWin32 : error : '{name}': {ex.Message}");
                    errorCount++;
                }
            }

            Console.WriteLine($"Processing complete. Processed: {processedCount}, Skipped: {skippedCount}, Errors: {errorCount}");
            return errorCount == 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to process NativeMethods.txt file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates and writes source files to the output directory.
    /// </summary>
    /// <param name="superGenerator">The super generator instance.</param>
    /// <param name="outputPath">Output directory for generated files.</param>
    /// <returns>True if generation succeeded, false otherwise.</returns>
    private static bool GenerateAndWriteFiles(SuperGenerator superGenerator, DirectoryInfo outputPath)
    {
        try
        {
            // Ensure output directory exists
            outputPath.Create();
            Console.WriteLine($"Output directory: {outputPath.FullName}");

            var compilationUnits = superGenerator.GetCompilationUnits(CancellationToken.None);
            int fileCount = 0;

            foreach (KeyValuePair<string, CompilationUnitSyntax> unit in compilationUnits)
            {
                string fileName = unit.Key;
                string filePath = Path.Combine(outputPath.FullName, fileName);

                // Write the file
                string sourceText = unit.Value.GetText(Encoding.UTF8).ToString();
                File.WriteAllText(filePath, sourceText, Encoding.UTF8);

                Console.WriteLine($"Generated: {fileName} ({sourceText.Length} characters)");
                fileCount++;
            }

            Console.WriteLine($"Successfully generated {fileCount} source files.");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to generate and write files: {ex.Message}");
            return false;
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GeneratorOptions))]
    internal partial class GeneratorOptionsSerializerContext : JsonSerializerContext
    {
    }
}
