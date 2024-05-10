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

        public Test([CallerFilePath] string? testFile = null, [CallerMemberName] string? testMethod = null)
        {
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
        public string? NativeMethodsJson { get; set; }

        public GeneratorConfiguration GeneratorConfiguration { get; set; } = GeneratorConfiguration.Default;

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
    }
}
