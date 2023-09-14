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

        static AttributeSyntax? FindDllImportAttribute(SyntaxList<AttributeListSyntax> attributeLists) => attributeLists.SelectMany(al => al.Attributes).FirstOrDefault(a => a.Name.ToString() == "DllImport");
    }

    private IEnumerable<MethodDeclarationSyntax> GenerateMethod(string methodName)
    {
        this.GenerateApi(methodName);
        return this.FindGeneratedMethod(methodName);
    }
}
