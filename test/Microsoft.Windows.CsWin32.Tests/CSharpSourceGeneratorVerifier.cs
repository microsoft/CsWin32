// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;

internal static class CSharpSourceGeneratorVerifier
{
    internal class Test : CSharpSourceGeneratorTest<SourceGenerator, DefaultVerifier>
    {
        private readonly string testFile;
        private readonly string testMethod;

        public Test(ITestOutputHelper logger, [CallerFilePath] string? testFile = null, [CallerMemberName] string? testMethod = null)
        {
            this.Logger = logger;
            this.testFile = testFile ?? throw new ArgumentNullException(nameof(testFile));
            this.testMethod = testMethod ?? throw new ArgumentNullException(nameof(testMethod));

            // We don't mean to use record/playback verification.
            this.TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;

            this.ReferenceAssemblies = MyReferenceAssemblies.NetStandard20;
            this.TestState.Sources.Add(string.Empty);
        }

        public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp9;

        public string? NativeMethodsTxt { get; set; }

        [StringSyntax(StringSyntaxAttribute.Json)]
        public string? NativeMethodsJson { get; set; } = """
            {
            }
            """;

        public GeneratorConfiguration GeneratorConfiguration { get; set; } = GeneratorConfiguration.Default;

        internal ITestOutputHelper Logger { get; }

        protected override IEnumerable<Type> GetSourceGenerators() => [typeof(SourceGenerator)];

        protected override ParseOptions CreateParseOptions() => ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(this.LanguageVersion);

        protected override CompilationOptions CreateCompilationOptions()
        {
            var compilationOptions = (CSharpCompilationOptions)base.CreateCompilationOptions();
            return compilationOptions
                .WithAllowUnsafe(true)
                .WithWarningLevel(99)
                .WithSpecificDiagnosticOptions(compilationOptions.SpecificDiagnosticOptions.SetItem("CS1591", ReportDiagnostic.Suppress));
        }

        protected override Task RunImplAsync(CancellationToken cancellationToken)
        {
            if (this.NativeMethodsTxt is not null)
            {
                this.TestState.AdditionalFiles.Add(("NativeMethods.txt", this.NativeMethodsTxt));
            }

            if (this.NativeMethodsJson is not null)
            {
                this.TestState.AdditionalFiles.Add(("NativeMethods.json", this.NativeMethodsJson));
            }

            this.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", this.GeneratorConfiguration.ToGlobalConfigString()));

            return base.RunImplAsync(cancellationToken);
        }

#pragma warning disable SA1316 // Tuple element names should use correct casing
        protected override async Task<(Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics)> GetProjectCompilationAsync(Project project, IVerifier verifier, CancellationToken cancellationToken)
#pragma warning restore SA1316 // Tuple element names should use correct casing
        {
            var (compilation, diagnostics) = await base.GetProjectCompilationAsync(project, verifier, cancellationToken);

            var documentsWithDiagnostics = compilation.GetDiagnostics(cancellationToken).Select(d => d.Location.SourceTree).Distinct().ToArray();
            foreach (SyntaxTree? source in documentsWithDiagnostics)
            {
                if (source is not null)
                {
                    GeneratorTestBase.LogGeneratedCode(source, this.Logger);
                }
            }

            return (compilation, diagnostics);
        }
    }
}
