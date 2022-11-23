// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

[Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
public class FullGenerationTests : GeneratorTestBase
{
    public FullGenerationTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void FullGeneration(MarshalingOptions marshaling, bool useIntPtrForComOutPtr, [CombinatorialMemberData(nameof(AnyCpuArchitectures))] Platform platform)
    {
        var generatorOptions = new GeneratorOptions
        {
            AllowMarshaling = marshaling >= MarshalingOptions.MarshalingWithoutSafeHandles,
            UseSafeHandles = marshaling == MarshalingOptions.FullMarshaling,
            ComInterop = new() { UseIntPtrForComOutPointers = useIntPtrForComOutPtr },
        };
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator(generatorOptions);
        this.generator.GenerateAll(CancellationToken.None);
        this.CollectGeneratedCode(this.generator);
        this.generator = null; // release memory
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }
}
