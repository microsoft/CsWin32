// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Win32.CodeGen
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.Windows.CsWin32;

    internal class Program
    {
        private static void Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Canceling...");
                cts.Cancel();
                e.Cancel = true;
            };

            Console.WriteLine("Initializing generator...");

            try
            {
                string outputDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "output");
                if (Directory.Exists(outputDirectory))
                {
                    foreach (string file in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                }
                else
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var sw = Stopwatch.StartNew();
                using var metadataStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Win32.winmd"));
                using var generator = new Generator(
                    metadataStream,
                    new GeneratorOptions
                    {
                        WideCharOnly = true,
                        EmitSingleFile = true,
                    },
                    parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
                Console.WriteLine("Generating code... (press Ctrl+C to cancel)");
                if (args.Length > 0)
                {
                    foreach (string name in args)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (!generator.TryGenerate(name, cts.Token))
                        {
                            Console.Error.WriteLine("WARNING: No match for " + name);
                        }
                    }
                }
                else
                {
                    generator.GenerateAll(cts.Token);
                }

                Console.WriteLine("Gathering source files...");
                var compilationUnits = generator.GetCompilationUnits(cts.Token);
                Console.WriteLine("Emitting source files...");
                compilationUnits.AsParallel().WithCancellation(cts.Token).ForAll(unit =>
                {
                    string outputPath = Path.Combine(outputDirectory, unit.Key);
                    Console.WriteLine("Writing output file: {0}", outputPath);
                    using var generatedSourceStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    using var generatedSourceWriter = new StreamWriter(generatedSourceStream, Encoding.UTF8);
                    unit.Value.WriteTo(generatedSourceWriter);
                });

                Console.WriteLine("Generation time: {0}", sw.Elapsed);
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == cts.Token)
            {
                Console.Error.WriteLine("Canceled.");
            }
        }
    }
}
