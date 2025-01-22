// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class ExternMethodTests : GeneratorTestBase
{
    public ExternMethodTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void AssociatedEnumOnParameter()
    {
        MethodDeclarationSyntax method = Assert.Single(this.GenerateMethod("SHObjectProperties"), IsOrContainsExternMethod);
        ParameterSyntax enumParam = method.ParameterList.Parameters[1];
        Assert.Equal("SHOP_TYPE", Assert.IsType<QualifiedNameSyntax>(enumParam.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void AssociatedEnumOnReturnValue()
    {
        MethodDeclarationSyntax method = Assert.Single(this.GenerateMethod("PathCleanupSpec"), IsOrContainsExternMethod);
        Assert.Equal("PCS_RET", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText);
    }

    [Fact]
    public void AssociatedEnumOnParameterWithVoidReturn()
    {
        this.GenerateMethod("SHChangeNotify");
    }

    /// <summary>
    /// Verifies that we can generate APIs from the WDK, which includes references to the SDK.
    /// </summary>
    [Fact]
    public void WdkMethod_NtCreateFile()
    {
        this.GenerateMethod("NtCreateFile");
    }

    [Theory, CombinatorialData]
    public void SetLastError_ByMarshaling(
        bool allowMarshaling,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi("GetVersionEx");

        bool expectMarshalingAttribute = allowMarshaling || tfm is "net472" or "netstandard2.0";
        MethodDeclarationSyntax originalMethod = this.FindGeneratedMethod("GetVersionEx").Single(m => m.ParameterList.Parameters[0].Type is PointerTypeSyntax);
        AttributeSyntax? attribute = FindDllImportAttribute(originalMethod.AttributeLists) ?? FindDllImportAttribute(originalMethod.Body?.Statements.OfType<LocalFunctionStatementSyntax>().SingleOrDefault()?.AttributeLists ?? default);
        Assert.NotNull(attribute);
        Assert.Equal(expectMarshalingAttribute, attribute.ArgumentList!.Arguments.Any(a => a.NameEquals?.Name.Identifier.ValueText == "SetLastError"));
    }

    [Fact]
    public void CustomEntryPointCarriesOver()
    {
        this.GenerateApi("FileIconInit");
        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod("FileIconInit"));
        AttributeSyntax? attribute = FindDllImportAttribute(method.AttributeLists);
        Assert.NotNull(attribute);
        AttributeArgumentSyntax arg = Assert.Single(attribute.ArgumentList!.Arguments, a => a.NameEquals?.Name.Identifier.ValueText == "EntryPoint");
        Assert.True(arg.Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression, Token: { Value: "#660" } });
    }

    [Fact]
    public void DefaultEntryPointIsNotEmitted()
    {
        this.GenerateApi("GetTickCount");
        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod("GetTickCount"));
        AttributeSyntax? attribute = FindDllImportAttribute(method.AttributeLists);
        Assert.NotNull(attribute);
        Assert.DoesNotContain(attribute.ArgumentList!.Arguments, a => a.NameEquals?.Name.Identifier.ValueText == "EntryPoint");
    }

    [Fact]
    public void ReferencesToStructWithFlexibleArrayAreAlwaysPointers()
    {
        this.GenerateApi("CreateDIBSection");
        Assert.All(this.FindGeneratedMethod("CreateDIBSection"), m => Assert.IsType<PointerTypeSyntax>(m.ParameterList.Parameters[1].Type));

        // Assert that the 'unmanaged' declaration of the struct is the *only* declaration.
        Assert.Single(this.FindGeneratedType("BITMAPINFO"));
        Assert.Empty(this.FindGeneratedType("BITMAPINFO_unmanaged"));
    }

    [Theory, PairwiseData]
    public void OverloadResolutionAttributeUsage(
        bool useMatchingLanguageVersion,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))] string tfm)
    {
        // Set up the test under the appropriate TFM and either a matching language version or C# 13,
        // which is the first version that supports the OverloadResolutionPriorityAttribute.
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(
            useMatchingLanguageVersion ? (GetLanguageVersionForTfm(tfm) ?? LanguageVersion.Latest) : LanguageVersion.CSharp13);
        this.generator = this.CreateGenerator();

        this.GenerateApi("EnumDisplayMonitors");

        // Emit usage that would be ambiguous without the OverloadResolutionPriorityAttribute.
        this.compilation = this.AddCode("""
            using Windows.Win32;
            using Windows.Win32.Foundation;
            using Windows.Win32.Graphics.Gdi;
            
            class Foo
            {
                static void Use()
                {
                    PInvoke.EnumDisplayMonitors(default, null, default, default);
                }
            }
            """);

        Func<Diagnostic, bool>? isAcceptable = this.parseOptions.LanguageVersion >= LanguageVersion.CSharp13
            ? null // C# 13 and later should not produce any diagnostics.
            : diag => diag.Descriptor.Id == "CS0121";
        this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false, acceptable: isAcceptable);
    }

    private static AttributeSyntax? FindDllImportAttribute(SyntaxList<AttributeListSyntax> attributeLists) => attributeLists.SelectMany(al => al.Attributes).FirstOrDefault(a => a.Name.ToString() == "DllImport");

    private IEnumerable<MethodDeclarationSyntax> GenerateMethod(string methodName)
    {
        this.GenerateApi(methodName);
        return this.FindGeneratedMethod(methodName);
    }
}
