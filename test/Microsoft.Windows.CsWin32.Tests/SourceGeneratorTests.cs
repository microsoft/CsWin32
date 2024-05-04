// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VerifyCS = CSharpSourceGeneratorVerifier;

public class SourceGeneratorTests
{
    [Fact]
    public async Task UnparseableNativeMethodsJson()
    {
        await new VerifyCS.Test
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
        }.RunAsync();
    }

    /// <summary>
    /// Asserts that no warning is produced even without the required reference, when no source is being generated anyway.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_NoGeneratedCode()
    {
        await new VerifyCS.Test
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default,
        }.RunAsync();
    }

    /// <summary>
    /// Asserts that a warning is produced when targeting a framework that our generated code requires the System.Memory reference for, but the reference is missing.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_NetFx472()
    {
        await new VerifyCS.Test
        {
            ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net472.Default,
            NativeMethodsTxt = "CreateFile",
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SourceGenerator.MissingRecommendedReference.Id, DiagnosticSeverity.Warning),
            },
        }.RunAsync();
    }

    /// <summary>
    /// Asserts that when targeting a framework that implicitly includes the references we need, no warning is generated.
    /// </summary>
    [Fact]
    public async Task MissingSystemMemoryReference_WithGeneratedCode_Net60()
    {
        await new VerifyCS.Test
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net60,
            NativeMethodsTxt = "CreateFile",
        }.RunAsync();
    }
}
