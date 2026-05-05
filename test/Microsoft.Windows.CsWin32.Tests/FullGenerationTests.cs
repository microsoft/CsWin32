// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

/// <summary>
/// Tests that generate everything possible, or subsets of everything.
/// </summary>
/// <remarks>
/// <para>
/// These tests tend to be slow and require ~4GB of memory each. They are tagged with
/// <c>[Trait("TestCategory", "HighMemory")]</c> and run in a dedicated CI job with
/// limited parallelism to avoid OOM on memory-constrained agents.
/// </para>
/// <para>
/// Each test is assigned to a <c>TestShard</c> that determines which CI agent runs it.
/// The shard names are: <c>FastMethods</c>, <c>EverythingModern</c>, <c>EverythingLegacy</c>.
/// Tests without a shard assignment run in a <c>Default</c> catch-all shard.
/// When adding new HighMemory tests, assign them to an appropriate shard.
/// </para>
/// </remarks>
public class FullGenerationTests : GeneratorTestBase
{
    public FullGenerationTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Gets the modern target frameworks (net8.0+). Used to split <c>Everything</c> across shards.
    /// </summary>
    public static string[] ModernTfms => new[] { "net8.0", "net9.0", "net10.0" };

    /// <summary>
    /// Gets the legacy target frameworks (pre-.NET Core). Used to split <c>Everything</c> across shards.
    /// </summary>
    public static string[] LegacyTfms => new[] { "net472", "netstandard2.0" };

    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FastMethods")]
    [Fact]
    public void Everything_NoFriendlyOverloads()
    {
        this.TestHelper(new GeneratorOptions { FriendlyOverloads = new() { Enabled = false } }, Platform.X64, "net8.0", generator => generator.GenerateAll(CancellationToken.None));
    }

    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "EverythingModern")]
    [Theory, PairwiseData]
    public void Everything_ModernTfms(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        [CombinatorialMemberData(nameof(AnyCpuArchitectures))] Platform platform,
        [CombinatorialMemberData(nameof(ModernTfms))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr), platform, tfm, generator => generator.GenerateAll(CancellationToken.None));
    }

    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "EverythingLegacy")]
    [Theory, PairwiseData]
    public void Everything_LegacyTfms(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        [CombinatorialMemberData(nameof(AnyCpuArchitectures))] Platform platform,
        [CombinatorialMemberData(nameof(LegacyTfms))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr), platform, tfm, generator => generator.GenerateAll(CancellationToken.None));
    }

    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FastMethods")]
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
    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FastMethods")]
    public void ExternMethods(
        MarshalingOptions marshaling,
        bool useIntPtrForComOutPtr,
        bool includePointerOverloads,
        [CombinatorialMemberData(nameof(SpecificCpuArchitectures))] Platform platform,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        this.TestHelper(OptionsForMarshaling(marshaling, useIntPtrForComOutPtr, includePointerOverloads), platform, tfm, generator => generator.GenerateAllExternMethods(CancellationToken.None));
    }

    [Fact]
    public void Macros()
    {
        this.TestHelper(new GeneratorOptions(), Platform.X64, DefaultTFM, generator => generator.GenerateAllMacros(CancellationToken.None));
    }

    private static GeneratorOptions OptionsForMarshaling(MarshalingOptions marshaling, bool useIntPtrForComOutPtr, bool includePointerOverloads = false) => new()
    {
        AllowMarshaling = marshaling >= MarshalingOptions.MarshalingWithoutSafeHandles,
        UseSafeHandles = marshaling == MarshalingOptions.FullMarshaling,
        ComInterop = new()
        {
            UseIntPtrForComOutPointers = useIntPtrForComOutPtr,
        },
        FriendlyOverloads = new()
        {
            IncludePointerOverloads = includePointerOverloads,
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
