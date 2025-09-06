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

    [Theory, CombinatorialData]
    public void CrossWinMD_IInspectable(
        [CombinatorialValues([false, true])] bool allowMarshaling,
        [CombinatorialValues([null, "TestPInvoke"])] string pinvokeClassName,
        [CombinatorialValues(["net472", "net8.0"])] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        GeneratorOptions options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        if (pinvokeClassName is not null)
        {
            options = options with { ClassName = pinvokeClassName };
        }

        this.generator = this.CreateSuperGenerator([.. DefaultMetadataPaths, CustomIInspectableMetadataPath], options);
        this.GenerateApi("ITestDerivedFromInspectable");
    }
}
