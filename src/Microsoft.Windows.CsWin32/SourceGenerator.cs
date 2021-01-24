// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>
    /// Generates the source code for the p/invoke methods and supporting types into some C# project.
    /// </summary>
    [Generator]
    public class SourceGenerator : ISourceGenerator
    {
        private const string NativeMethodsTxtAdditionalFileName = "NativeMethods.txt";
        private const string NativeMethodsJsonAdditionalFileName = "NativeMethods.json";
        private static readonly DiagnosticDescriptor NoMatchingMethodOrType = new DiagnosticDescriptor(
            "PInvoke001",
            "No matching method or type found",
            "Method or type \"{0}\" not found.",
            "Functionality",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor NoMethodsForModule = new DiagnosticDescriptor(
            "PInvoke001",
            "No module found",
            "No methods found under module \"{0}\".",
            "Functionality",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsafeCodeRequired = new DiagnosticDescriptor(
            "PInvoke002",
            "AllowUnsafeCode",
            "AllowUnsafeBlocks must be set to 'true' in the project file for many APIs. Compiler errors may result.",
            "Functionality",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Many generated types or P/Invoke methods require use of pointers, so the receiving compilation must allow unsafe code.");

        private static readonly DiagnosticDescriptor BannedApi = new DiagnosticDescriptor(
            "PInvoke003",
            "BannedAPI",
            "This API will not be generated. {0}",
            "Functionality",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <inheritdoc/>
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        /// <inheritdoc/>
        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.Compilation is CSharpCompilation))
            {
                return;
            }

            if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.MicrosoftWindowsSdkWin32MetadataBasePath", out string? metadataPath) ||
                string.IsNullOrWhiteSpace(metadataPath))
            {
                return;
            }

            GeneratorOptions? options = null;
            AdditionalText? nativeMethodsJsonFile = context.AdditionalFiles
                .FirstOrDefault(af => string.Equals(Path.GetFileName(af.Path), NativeMethodsJsonAdditionalFileName, StringComparison.OrdinalIgnoreCase));
            if (nativeMethodsJsonFile is object)
            {
                string optionsJson = nativeMethodsJsonFile.GetText(context.CancellationToken)!.ToString();
                options = JsonSerializer.Deserialize<GeneratorOptions>(optionsJson, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
            }

            AdditionalText? nativeMethodsTxtFile = context.AdditionalFiles
                .FirstOrDefault(af => string.Equals(Path.GetFileName(af.Path), NativeMethodsTxtAdditionalFileName, StringComparison.OrdinalIgnoreCase));
            if (nativeMethodsTxtFile is null)
            {
                return;
            }

            using var metadataStream = File.OpenRead(Path.Combine(metadataPath, "Windows.Win32.winmd"));
            var compilation = (CSharpCompilation)context.Compilation;
            var parseOptions = (CSharpParseOptions)context.ParseOptions;

            if (!compilation.Options.AllowUnsafe)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsafeCodeRequired, location: null));
            }

            using var generator = new Generator(metadataStream, options, compilation, parseOptions);

            SourceText? nativeMethodsTxt = nativeMethodsTxtFile.GetText(context.CancellationToken);
            if (nativeMethodsTxt is null)
            {
                return;
            }

            foreach (TextLine line in nativeMethodsTxt.Lines)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string name = line.ToString();
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("//", StringComparison.InvariantCulture))
                {
                    continue;
                }

                name = name.Trim();
                var location = Location.Create(nativeMethodsTxtFile.Path, line.Span, nativeMethodsTxt.Lines.GetLinePositionSpan(line.Span));
                if (Generator.BannedAPIs.TryGetValue(name, out string? reason))
                {
                    context.ReportDiagnostic(Diagnostic.Create(BannedApi, location, reason));
                }
                else if (name.EndsWith(".*", StringComparison.Ordinal))
                {
                    var moduleName = name.Substring(0, name.Length - 2);
                    if (!generator.TryGenerateAllExternMethods(moduleName, context.CancellationToken))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NoMethodsForModule, location, moduleName));
                    }
                }
                else if (!generator.TryGenerate(name, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(NoMatchingMethodOrType, location, name));
                }
            }

            var compilationUnits = generator.GetCompilationUnits(context.CancellationToken);
            foreach (var unit in compilationUnits)
            {
                context.AddSource(unit.Key, unit.Value.ToFullString());
            }
        }
    }
}
