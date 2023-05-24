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
        Assert.All(this.FindGeneratedMethod(name), m => Assert.Equal(5,  m.ParameterList.Parameters.Count));
    }

    [Fact]
    public void SpecializedRAIIFree()
    {
        const string Method = "CreateActCtx";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(Method, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("ReleaseActCtxSafeHandle", Assert.IsType<IdentifierNameSyntax>(method.ReturnType).Identifier.ValueText);
    }

    private void Generate(string name)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(name, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }
}
