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
}
