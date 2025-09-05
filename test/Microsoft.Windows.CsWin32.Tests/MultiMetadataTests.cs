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
        this.generator = this.CreateSuperGenerator([.. DefaultMetadataPaths, ServiceFabricMetadataPath], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi("IFabricStringResult");
    }

    [Theory, PairwiseData]
    public void CrossWinMD_IInspectable(bool allowMarshaling)
    {
        this.generator = this.CreateSuperGenerator([.. DefaultMetadataPaths, CustomIInspectableMetadataPath], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi("ITestDerivedFromInspectable");
    }
}
