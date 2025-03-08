// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class InlineArrayTests : GeneratorTestBase
{
    public InlineArrayTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void FixedLengthInlineArray(
        bool allowMarshaling,
        bool multitargetingAPIs,
        [CombinatorialValues("net35", "net472", "net8.0")] string tfm,
        [CombinatorialValues(/*char*/"RM_PROCESS_INFO", /*custom unmanaged*/"ARRAYDESC")] string api)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling, MultiTargetingFriendlyAPIs = multitargetingAPIs });

        // TODO we need to test
        // another IEquatable primitive,
        // a non-IEquatable primitive (e.g. IntPtr before .NET 5),
        // and a custom managed.
        //// TODO: code here

        Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, CombinatorialData]
    public async Task UnsafeApiReferencesOnlyWhenAvailable(
        bool referenceUnsafe,
        bool referenceMemory,
        [CombinatorialValues("net35", "net472", "netstandard2.0")] string tfm)
    {
        ReferenceAssemblies referenceAssemblies = tfm switch
        {
            "net35" => ReferenceAssemblies.NetFramework.Net35.WindowsForms,
            "net472" => ReferenceAssemblies.NetFramework.Net472.WindowsForms,
            "netstandard2.0" => ReferenceAssemblies.NetStandard.NetStandard20,
            _ => throw new ArgumentOutOfRangeException(nameof(tfm)),
        };
        if (referenceUnsafe)
        {
            referenceAssemblies = referenceAssemblies.AddPackages([MyReferenceAssemblies.ExtraPackages.Unsafe]);
        }

        if (referenceMemory)
        {
            referenceAssemblies = referenceAssemblies.AddPackages([MyReferenceAssemblies.ExtraPackages.Memory]);
        }

        this.compilation = await this.CreateCompilationAsync(referenceAssemblies, Platform.AnyCpu);
        this.GenerateApi("THUMBBUTTON");
    }

    [Theory, PairwiseData]
    public void FixedLengthInlineArray_Pointers(
        bool allowMarshaling,
        bool multitargetingAPIs,
        [CombinatorialValues("net35", "net472", "net8.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling, MultiTargetingFriendlyAPIs = multitargetingAPIs });
        Assert.True(this.generator.TryGenerate("RTM_ENTITY_EXPORT_METHODS", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void FixedLengthInlineArray_TwoRequested()
    {
        this.generator = this.CreateGenerator();

        // These two APIs are specially selected because they are both char arrays, and thus would both request the SliceAtNull extension method's generation.
        Assert.True(this.generator.TryGenerate("RM_PROCESS_INFO", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("WER_REPORT_INFORMATION", CancellationToken.None));

        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Verify that inline arrays that share the same length and type are only declared once and shared with all users.
        Assert.Single(this.FindGeneratedType("__char_64"));
    }

    [Theory, PairwiseData]
    public void FixedLengthInlineArrayIn_MODULEENTRY32(bool allowMarshaling)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate("MODULEENTRY32", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        var decl = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("MODULEENTRY32"));
        var field = this.FindFieldDeclaration(decl, "szModule");
        Assert.True(field.HasValue);
        var fieldType = Assert.IsType<QualifiedNameSyntax>(field!.Value.Field.Declaration.Type);
        Assert.IsType<StructDeclarationSyntax>(Assert.Single(this.FindGeneratedType(fieldType.Right.Identifier.ValueText)));
    }
}
