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
            this.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", ConstructGlobalConfigString()));
        }

        public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp9;

        public string? NativeMethodsTxt { get; set; }

        [StringSyntax(StringSyntaxAttribute.Json)]
        public string? NativeMethodsJson { get; set; }

        protected override IEnumerable<Type> GetSourceGenerators()
        {
            yield return typeof(SourceGenerator);
        }

        protected override ParseOptions CreateParseOptions()
        {
            return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(this.LanguageVersion);
        }

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

            return base.RunImplAsync(cancellationToken);
        }

        private static string ConstructGlobalConfigString(bool omitDocs = false)
        {
            StringBuilder globalConfigBuilder = new();
            globalConfigBuilder.AppendLine("is_global = true");
            globalConfigBuilder.AppendLine();
            globalConfigBuilder.AppendLine($"build_property.CsWin32InputMetadataPaths = {JoinAssemblyMetadata("ProjectionMetadataWinmd")}");
            if (!omitDocs)
            {
                globalConfigBuilder.AppendLine($"build_property.CsWin32InputDocPaths = {JoinAssemblyMetadata("ProjectionDocs")}");
            }

            return globalConfigBuilder.ToString();

            static string JoinAssemblyMetadata(string name)
            {
                return string.Join(";", typeof(GeneratorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(metadata => metadata.Key == name).Select(metadata => metadata.Value));
            }
        }
    }
}
