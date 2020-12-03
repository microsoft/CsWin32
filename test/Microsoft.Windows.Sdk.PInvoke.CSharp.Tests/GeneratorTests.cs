// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Windows.Sdk.PInvoke.CSharp;
using Xunit;
using Xunit.Abstractions;

public class GeneratorTests : IDisposable, IAsyncLifetime
{
    private static readonly string FileSeparator = new string('=', 140);
    private readonly ITestOutputHelper logger;
    private CSharpCompilation compilation;
    private CSharpParseOptions parseOptions;
    private Generator? generator;

    public GeneratorTests(ITestOutputHelper logger)
    {
        this.logger = logger;

        this.parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.CSharp9);
        this.compilation = null!; // set in InitializeAsync
    }

    public async Task InitializeAsync()
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.NetStandard.NetStandard20.ResolveAsync(LanguageNames.CSharp, default);

        // CONSIDER: How can I pass in the source generator itself, with AdditionalFiles, so I'm exercising that code too?
        this.compilation = CSharpCompilation.Create(
            assemblyName: "test",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        this.generator?.Dispose();
    }

    [Fact]
    public void SimplestMethod()
    {
        this.generator = new Generator(compilation: this.compilation, parseOptions: this.parseOptions);
        Assert.True(this.generator.TryGenerateExternMethod("GetTickCount"));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    private void CollectGeneratedCode(Generator generator)
    {
        var compilationUnits = generator.GetCompilationUnits(CancellationToken.None);
        var syntaxTrees = new List<CSharpSyntaxTree>(compilationUnits.Count);
        foreach (var unit in compilationUnits)
        {
            this.logger.WriteLine($"{unit.Key} content:");
            this.logger.WriteLine(FileSeparator);
            using var lineWriter = new NumberedLineWriter(this.logger);
            unit.Value.WriteTo(lineWriter);
            this.logger.WriteLine(FileSeparator);

            syntaxTrees.Add((CSharpSyntaxTree)CSharpSyntaxTree.Create(unit.Value, this.parseOptions, path: unit.Key));
        }

        this.compilation = this.compilation.AddSyntaxTrees(syntaxTrees);
    }

    private void AssertNoDiagnostics()
    {
        this.AssertNoDiagnostics(this.compilation.GetDiagnostics());

        // TODO: do I need to Emit as well to get *all* the diagnostics?
        var emitResult = this.compilation.Emit(peStream: Stream.Null, xmlDocumentationStream: Stream.Null);
        this.AssertNoDiagnostics(emitResult.Diagnostics);
        Assert.True(emitResult.Success);
    }

    private void AssertNoDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }

        Assert.Empty(diagnostics);
    }
}
