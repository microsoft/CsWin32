// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class FriendlyOverloadTests : GeneratorTestBase
{
    public FriendlyOverloadTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void WriteFile()
    {
        const string name = "WriteFile";
        this.Generate(name);
        Assert.Contains(this.FindGeneratedMethod(name), m => m.ParameterList.Parameters.Count == 4);
    }

    [Fact]
    public void SHGetFileInfo()
    {
        // This method uses MemorySize but for determining the size of a struct that another parameter points to.
        // We cannot know the size of that, since it may be a v1 struct, a v2 struct, etc.
        // So assert that no overload has fewer parameters.
        const string name = "SHGetFileInfo";
        this.Generate(name);
        Assert.All(this.FindGeneratedMethod(name), m => Assert.Equal(5, m.ParameterList.Parameters.Count));
    }

    [Fact]
    public void SpecializedRAIIFree_ReturnValue()
    {
        const string Method = "CreateActCtx";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("ReleaseActCtxSafeHandle", Assert.IsType<IdentifierNameSyntax>(method.ReturnType).Identifier.ValueText);
    }

    [Fact]
    public void SpecializedRAIIFree_OutParameter()
    {
        const string Method = "DsGetDcOpen";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("DsGetDcCloseWSafeHandle", Assert.IsType<IdentifierNameSyntax>(method.ParameterList.Parameters.Last().Type).Identifier.ValueText);
    }

    [Fact]
    public void InAttributeOnArraysProjectedAsReadOnlySpan()
    {
        const string Method = "RmRegisterResources";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal(3, method.ParameterList.Parameters.Count(p => p.Type is GenericNameSyntax { Identifier.ValueText: "ReadOnlySpan" }));
    }

    [Fact]
    public void OutPWSTR_Parameters_AsSpan()
    {
        const string name = "GetWindowText";
        this.Generate(name);
        MethodDeclarationSyntax friendlyOverload = Assert.Single(this.FindGeneratedMethod(name), m => m.ParameterList.Parameters.Count == 2);
        Assert.Equal("Span<char>", friendlyOverload.ParameterList.Parameters[1].Type?.ToString());
    }

    [Theory]
    [InlineData("WSManGetSessionOptionAsString")] // Uses the reserved keyword 'string' as a parameter name
    [InlineData("RmRegisterResources")] // Parameter with PCWSTR* (an array of native strings)
    public void InterestingAPIs(string name)
    {
        this.Generate(name);
    }

    private void Generate(string name)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.GenerateApi(name);
    }
}
