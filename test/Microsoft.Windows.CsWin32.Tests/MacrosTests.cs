// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class MacrosTests : GeneratorTestBase
{
    public MacrosTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void MacroAPIsGenerateWithAppropriateVisibility(bool publicVisibility)
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { Public = publicVisibility });
        Assert.True(this.generator.TryGenerate("MAKELONG", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        var makelongMethod = Assert.Single(this.FindGeneratedMethod("MAKELONG"));
        Assert.True(makelongMethod.Modifiers.Any(publicVisibility ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword));
    }

    [Theory]
    [MemberData(nameof(AvailableMacros))]
    public void MacroAPIsGenerate(string macro)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(macro, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedMethod(macro));
    }
}
