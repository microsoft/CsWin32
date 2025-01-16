// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpSourceGeneratorVerifier;

public class SourceGeneratorTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UnparseableNativeMethodsJson()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "CreateFile",
#pragma warning disable JSON001 // Invalid JSON pattern -- deliberate point of the test
            NativeMethodsJson = @"{ ""allowMarshaling"": f }",
#pragma warning restore JSON001 // Invalid JSON pattern
            TestState =
            {
                GeneratedSources =
                {
                    // Nothing generated, but no exceptions thrown that would lead Roslyn to disable the source generator in the IDE either.
                },
                ExpectedDiagnostics =
                {
                    new DiagnosticResult(SourceGenerator.OptionsParsingError.Id, DiagnosticSeverity.Error),
                },
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that no warning is produced even without the required reference, when no source is being generated anyway.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_NoGeneratedCode()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that a warning is produced when targeting a framework that our generated code requires the System.Memory reference for, but the reference is missing.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_NetFx472()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default,
            NativeMethodsTxt = "CreateFile",
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.MissingRecommendedReference.Id, DiagnosticSeverity.Warning),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that when targeting a framework that implicitly includes the references we need, no warning is generated.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_Net80()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            NativeMethodsTxt = "CreateFile",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Gdi32()
    {
        await new VerifyCS.Test(logger)
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            LanguageVersion = LanguageVersion.CSharp12,
            NativeMethodsTxt = "gdi32.*",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NonUniqueWinmdProjectionNames()
    {
        await new VerifyCS.Test(logger)
        {
            NativeMethodsTxt = "CreateFile",
            GeneratorConfiguration = GeneratorConfiguration.Default with
            {
                InputMetadataPaths = GeneratorConfiguration.Default.InputMetadataPaths.AddRange(GeneratorConfiguration.Default.InputMetadataPaths),
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.NonUniqueMetadataInputs.Id, DiagnosticSeverity.Error),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
