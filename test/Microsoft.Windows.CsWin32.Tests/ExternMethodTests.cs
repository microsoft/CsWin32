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
        const string Method = "SHObjectProperties";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(Method, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), IsOrContainsExternMethod);
        ParameterSyntax enumParam = method.ParameterList.Parameters[1];
        Assert.Equal("SHOP_TYPE", Assert.IsType<QualifiedNameSyntax>(enumParam.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void AssociatedEnumOnReturnValue()
    {
        const string Method = "PathCleanupSpec";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(Method, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), IsOrContainsExternMethod);
        Assert.Equal("PCS_RET", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText);
    }

    [Fact]
    public void AssociatedEnumOnParameterWithVoidReturn()
    {
        const string Method = "SHChangeNotify";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(Method, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Verifies that we can generate APIs from the WDK, which includes references to the SDK.
    /// </summary>
    [Fact]
    public void WdkMethod_NtCreateFile()
    {
        const string Method = "NtCreateFile";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(Method, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }
}
