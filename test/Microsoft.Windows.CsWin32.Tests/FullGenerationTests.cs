// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

/// <summary>
/// Tests that generate everything possible, or subsets of everything.
/// </summary>
/// <remarks>
/// These tests tend to be slow, and some require 4GB of memory, and thus some cannot be run on Azure Pipelines
/// as the test host process gets too large and gets terminated.
/// The <see cref="Everything"/> test should be run in all its combinations manually prior to sending a pull request.
/// </remarks>
public class FullGenerationTests : GeneratorTestBase
{
    public FullGenerationTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
    [Fact]
    public void Everything_NoFriendlyOverloads()
    {
        this.TestHelper(new GeneratorOptions { FriendlyOverloads = new() { Enabled = false } }, Platform.X64, "net8.0", generator => generator.GenerateAll(CancellationToken.None));
    }

    [Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
    [Theory, PairwiseData]
    public void Everything(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        [CombinatorialMemberData(nameof(AnyCpuArchitectures))] Platform platform,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr), platform, tfm, generator => generator.GenerateAll(CancellationToken.None));
    }

    [Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
    [Theory, PairwiseData]
    public void InteropTypes(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr), Platform.X64, tfm, generator => generator.GenerateAllInteropTypes(CancellationToken.None));
    }

    [Fact]
    public void Constants()
    {
        this.TestHelper(new GeneratorOptions(), Platform.X64, DefaultTFM, generator => generator.GenerateAllConstants(CancellationToken.None));
    }

    [Theory, PairwiseData]
    public void ExternMethods(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        [CombinatorialMemberData(nameof(SpecificCpuArchitectures))] Platform platform,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr), platform, tfm, generator => generator.GenerateAllExternMethods(CancellationToken.None));
    }

    [Fact]
    public void Macros()
    {
        this.TestHelper(new GeneratorOptions(), Platform.X64, DefaultTFM, generator => generator.GenerateAllMacros(CancellationToken.None));
    }

    private static GeneratorOptions OptionsForMarshaling(MarshalingOptions marshaling, bool useIntPtrForComOutPtr) => new()
    {
        AllowMarshaling = marshaling >= MarshalingOptions.MarshalingWithoutSafeHandles,
        UseSafeHandles = marshaling == MarshalingOptions.FullMarshaling,
        ComInterop = new()
        {
            UseIntPtrForComOutPointers = useIntPtrForComOutPtr,
        },
    };

    private void TestHelper(GeneratorOptions generatorOptions, Platform platform, string targetFramework, Action<IGenerator> generationCommands)
    {
        this.compilation = this.starterCompilations[targetFramework];
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));

        long? lastHeapSize = null;
        Stopwatch timer = Stopwatch.StartNew();
        LogStatus(null);

        this.generator = this.CreateGenerator(generatorOptions);
        LogStatus("creating generator");

        generationCommands(this.generator);
        LogStatus("creating syntax");

        this.CollectGeneratedCode(this.generator);
        this.generator = null; // release memory
        LogStatus("transferring syntax to compilation");

        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        LogStatus("emitting code and diagnostics");

        void LogStatus(string? completedStep)
        {
            GC.Collect();
            long heapSize = GC.GetGCMemoryInfo().HeapSizeBytes;
            string result;
            if (lastHeapSize.HasValue)
            {
                long diff = heapSize - lastHeapSize.Value;
                string diffPrefix = diff >= 0 ? "+" : string.Empty;
                result = $"{heapSize,13:n0} ({diffPrefix}{diff,13:n0})";
            }
            else
            {
                result = $"{heapSize,13:n0}";
            }

            lastHeapSize = heapSize;
            this.logger.WriteLine($"Heap: {result}, Elapsed: {timer.Elapsed} {completedStep}");
            timer.Restart();
        }
    }
}
