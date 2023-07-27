// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class MultiMetadataTests : GeneratorTestBase
{
    public MultiMetadataTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void BasicServiceFabric(bool allowMarshaling)
    {
        this.generator = this.CreateSuperGenerator(DefaultMetadataPaths.Concat(new[] { ServiceFabricMetadataPath }).ToArray(), DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate("IFabricStringResult", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }
}
