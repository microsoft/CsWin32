// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class HandleTests : GeneratorTestBase
{
    public HandleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void BSTR_FieldsDoNotBecomeSafeHandles(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.GenerateApi("DebugPropertyInfo");
        StructDeclarationSyntax structDecl = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("DebugPropertyInfo").Single());
        var bstrField = structDecl.Members.OfType<FieldDeclarationSyntax>().First(m => m.Declaration.Variables.Any(v => v.Identifier.ValueText == "m_bstrName"));
        Assert.Equal("BSTR", Assert.IsType<QualifiedNameSyntax>(bstrField.Declaration.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void NamespaceHandleGetsNoSafeHandle()
    {
        this.GenerateApi("CreatePrivateNamespace");
        Assert.Empty(this.FindGeneratedType("ClosePrivateNamespaceSafeHandle"));
    }

    [Fact]
    public void CreateFileUsesSafeHandles()
    {
        this.GenerateApi("CreateFile");

        Assert.Contains(
            this.FindGeneratedMethod("CreateFile"),
            createFileMethod => createFileMethod!.ReturnType.ToString() == "Microsoft.Win32.SafeHandles.SafeFileHandle"
                && createFileMethod.ParameterList.Parameters.Last().Type?.ToString() == "SafeHandle");
    }

    [Fact]
    public void OutHandleParameterBecomesSafeHandle()
    {
        this.generator = this.CreateGenerator();
        const string methodName = "TcAddFilter";
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        Assert.Contains(
            this.FindGeneratedMethod(methodName),
            method => method!.ParameterList.Parameters[2].Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: nameof(Microsoft.Win32.SafeHandles.SafeFileHandle) } } });

        Assert.Contains(
            this.FindGeneratedMethod(methodName),
            method => method!.ParameterList.Parameters[0].Type is IdentifierNameSyntax { Identifier: { ValueText: nameof(SafeHandle) } });
    }

    [Fact]
    public void AvoidSafeHandles()
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { UseSafeHandles = false });
        this.GenerateApi("GetExitCodeThread");
        MethodDeclarationSyntax friendlyOverload = Assert.Single(this.FindGeneratedMethod("GetExitCodeThread"), m => m.ParameterList.Parameters[^1].Modifiers.Any(SyntaxKind.OutKeyword));
        Assert.Equal("HANDLE", Assert.IsType<QualifiedNameSyntax>(friendlyOverload.ParameterList.Parameters[0].Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void SafeHandleInWDK()
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions);
        this.GenerateApi("OROpenHive");
    }

    /// <summary>
    /// Verifies that MSIHANDLE is wrapped with a SafeHandle even though it is a 32-bit handle.
    /// This is safe because we never pass SafeHandle directly to extern methods, so we can fix the length of the parameter or return value.
    /// </summary>
    [Fact]
    public void MSIHANDLE_BecomesSafeHandle()
    {
        this.generator = this.CreateGenerator();
        this.GenerateApi("MsiGetLastErrorRecord");

        Assert.Contains(
            this.FindGeneratedMethod("MsiGetLastErrorRecord"),
            method => method!.ReturnType is QualifiedNameSyntax { Right: { Identifier: { ValueText: "MSIHANDLE" } } });

        Assert.Contains(
            this.FindGeneratedMethod("MsiGetLastErrorRecord_SafeHandle"),
            method => method!.ReturnType?.ToString() == "MsiCloseHandleSafeHandle");

        MethodDeclarationSyntax releaseMethod = this.FindGeneratedMethod("MsiCloseHandle").Single();
        Assert.Equal("MSIHANDLE", Assert.IsType<QualifiedNameSyntax>(releaseMethod!.ParameterList.Parameters[0].Type).Right.Identifier.ValueText);
    }

    [Theory]
    [InlineData("HANDLE")]
    [InlineData("HGDIOBJ")]
    public void HandleStructsHaveIsNullProperty(string handleName)
    {
        // A null HGDIOBJ has a specific meaning beyond just the concept of an invalid handle:
        // https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-selectobject#return-value
        this.AssertGeneratedMember(handleName, "IsNull", "internal bool IsNull => Value == default;");
    }

    [Theory]
    [InlineData("HANDLE")]
    [InlineData("HGDIOBJ")]
    [InlineData("HMODULE")]
    public void HandleStructsHaveStaticNullMember(string handleName)
    {
        // A null HGDIOBJ has a specific meaning beyond just the concept of an invalid handle:
        // https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-selectobject#return-value
        this.AssertGeneratedMember(handleName, "Null", $"internal static {handleName} Null => default;");
    }

    [Theory]
    [InlineData("HANDLE")]
    [InlineData("HGDIOBJ")]
    [InlineData("HWND")]
    public void HandleTypeDefsUseVoidStarAsFieldType(string handleType)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(handleType, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax hwnd = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(handleType).Single());
        FieldDeclarationSyntax field = hwnd.Members.OfType<FieldDeclarationSyntax>().Single();
        Assert.True(field.Declaration.Type is PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } } });
    }

    [Fact]
    public void SafeHandleOverloadsGeneratedFor_HGDIObj()
    {
        this.GenerateApi("GetObject");
        Assert.Contains(
            this.FindGeneratedMethod("GetObject"),
            method => method!.ParameterList.Parameters[0].Type is IdentifierNameSyntax { Identifier: { ValueText: "SafeHandle" } });
    }

    [Fact]
    public void ReleaseMethodGeneratedWithHandleStruct()
    {
        this.GenerateApi("HANDLE");
        Assert.True(this.IsMethodGenerated("CloseHandle"));
    }

    [Theory]
    [InlineData("ICOpen")]
    [InlineData("CM_Register_Notification")]
    [InlineData("AllocateAndInitializeSid")]
    public void ReleaseMethodGeneratedWithUncommonReturnType(string api)
    {
        this.GenerateApi(api);
    }

    [Theory]
    [InlineData("HDC")]
    public void InterestingHandles(string api) => this.GenerateApi(api);
}
