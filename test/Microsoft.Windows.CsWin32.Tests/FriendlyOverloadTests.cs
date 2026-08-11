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
        // So assert that no overload has fewer parameters or it has a Span parameter.
        const string name = "SHGetFileInfo";
        this.Generate(name);
        Assert.All(
            this.FindGeneratedMethod(name),
            m => Assert.True(
                m.ParameterList.Parameters.Count == 5 ||
                m.ParameterList.Parameters.Any(p => p.Type is GenericNameSyntax { Identifier.ValueText: "Span" })));
    }

    [Fact]
    public void SpecializedRAIIFree_ReturnValue()
    {
        const string Method = "CreateActCtx";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("ReleaseActCtxSafeHandle", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText);
    }

    [Fact]
    public void SpecializedRAIIFree_OutParameter()
    {
        const string Method = "DsGetDcOpen";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("DsGetDcCloseWSafeHandle", Assert.IsType<QualifiedNameSyntax>(method.ParameterList.Parameters.Last().Type).Right.Identifier.ValueText);
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

    [Fact]
    public void PCSTR_StringOverloadMarshalsNullTerminator()
    {
        const string name = "GetProcAddress";
        const string value = "test";
        this.Generate(name);
        MethodDeclarationSyntax externMethod = Assert.Single(this.FindGeneratedMethod(name), IsOrContainsExternMethod);
        SyntaxToken pcstrParameter = externMethod.ParameterList.Parameters[1].Identifier;

        // A pinned SZArray stores its length one native word before its first element. Checking the
        // allocation length makes this test deterministic even when the byte after a short array is zero.
        MethodDeclarationSyntax testMethod = externMethod
            .WithAttributeLists([])
            .WithModifiers(externMethod.Modifiers.Replace(
                externMethod.Modifiers.Single(m => m.IsKind(SyntaxKind.ExternKeyword)),
                SyntaxFactory.Token(SyntaxKind.UnsafeKeyword)))
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"if (*(int*)({pcstrParameter}.Value - sizeof(nint)) != {value.Length + 1} || {pcstrParameter}.Value[{value.Length}] != 0) throw new InvalidOperationException();"),
                SyntaxFactory.ParseStatement("return default;")))
            .WithSemicolonToken(default);
        SyntaxTree testTree = externMethod.SyntaxTree.WithRootAndOptions(
            externMethod.SyntaxTree.GetRoot(TestContext.Current.CancellationToken).ReplaceNode(externMethod, testMethod),
            externMethod.SyntaxTree.Options);
        CSharpCompilation testCompilation = this.compilation.ReplaceSyntaxTree(externMethod.SyntaxTree, testTree);

        using var assemblyStream = new MemoryStream();
        var emitResult = testCompilation.Emit(assemblyStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        Assembly assembly = Assembly.Load(assemblyStream.ToArray());
        MethodInfo friendlyOverload = Assert.Single(
            assembly.GetType("Windows.Win32.PInvoke")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
            m => m.Name == name && m.GetParameters() is [_, { ParameterType: { } parameterType }] && parameterType == typeof(string));

        using var moduleHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(1), ownsHandle: false);
        friendlyOverload.Invoke(null, [moduleHandle, value]);
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
