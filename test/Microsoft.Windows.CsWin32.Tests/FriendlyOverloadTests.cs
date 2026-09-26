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

    /// <summary>
    /// Verifies documentation with public ABI types from one assembly and, optionally, an internal copy in another.
    /// </summary>
    [Theory]
    [InlineData("CreateWindowEx", false, LanguageVersion.CSharp12, true)]
    [InlineData("CreateWindowEx", false, LanguageVersion.CSharp12, false)]
    [InlineData("CreateWindowEx", false, LanguageVersion.CSharp14, false)]
    [InlineData("CreateWindowEx", false, LanguageVersion.CSharp14, true)]
    [InlineData("CreateWindowEx", true, LanguageVersion.CSharp14, false)]
    [InlineData("CreateWindowEx", true, LanguageVersion.CSharp14, true)]
    [InlineData("ConvertStringSecurityDescriptorToSecurityDescriptor", false, LanguageVersion.CSharp12, false)]
    [InlineData("ConvertStringSecurityDescriptorToSecurityDescriptor", false, LanguageVersion.CSharp12, true)]
    public void FriendlyOverloadDocumentationWithReferencedPCWSTR(string methodName, bool extensionReceiver, LanguageVersion languageVersion, bool includeHiddenPCWSTR)
    {
        this.parseOptions = this.parseOptions.WithLanguageVersion(languageVersion);
        CSharpCompilationOptions options = this.starterCompilations["net8.0"].Options;
        this.compilation = this.starterCompilations["net8.0"].WithOptions(options
            .WithPlatform(Platform.X64)
            .WithSpecificDiagnosticOptions(options.SpecificDiagnosticOptions
                .SetItem("CS1574", ReportDiagnostic.Error)
                .SetItem("CS1580", ReportDiagnostic.Error)));
        CSharpCompilation referencedProject = this.compilation.WithAssemblyName("ReferencedInterop");
        using (var referencedGenerator = this.CreateGenerator(new GeneratorOptions { Public = true }, referencedProject))
        {
            Assert.True(referencedGenerator.TryGenerate("FindWindow", TestContext.Current.CancellationToken));
            referencedProject = this.AddGeneratedCode(referencedProject, referencedGenerator);
        }

        this.AssertNoDiagnostics(referencedProject, logAllGeneratedCode: false);
        using var assemblyStream = new MemoryStream();
        Assert.True(referencedProject.Emit(assemblyStream, cancellationToken: TestContext.Current.CancellationToken).Success);
        this.compilation = this.compilation.AddReferences(MetadataReference.CreateFromImage(assemblyStream.ToArray()));
        if (includeHiddenPCWSTR)
        {
            // A dependency unrelated to CsWin32 can embed an internal ABI type with the same name.
            CSharpCompilation hiddenProject = this.starterCompilations["net8.0"]
                .WithAssemblyName("HiddenInterop")
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                    "namespace Windows.Win32.Foundation { internal struct PCWSTR { } }",
                    this.parseOptions,
                    cancellationToken: TestContext.Current.CancellationToken));
            using var hiddenStream = new MemoryStream();
            Assert.True(hiddenProject.Emit(hiddenStream, cancellationToken: TestContext.Current.CancellationToken).Success);
            this.compilation = this.compilation.AddReferences(MetadataReference.CreateFromImage(hiddenStream.ToArray()));
            Assert.Equal(2, this.compilation.GetTypesByMetadataName("Windows.Win32.Foundation.PCWSTR").Length);
        }

        this.generator = this.CreateGenerator(
            new GeneratorOptions
            {
                EmitSingleFile = false,
                ClassName = extensionReceiver ? "PInvokeExtensions" : "PInvoke",
                ExtensionReceiver = extensionReceiver ? "PInvoke" : null,
            },
            includeDocs: true);
        this.GenerateApi(methodName);
        Assert.Empty(this.FindGeneratedType("PCWSTR"));
        IEnumerable<MethodDeclarationSyntax> friendlyOverloads = this.FindGeneratedMethod(methodName).Where(m => !IsOrContainsExternMethod(m));
        Assert.NotEmpty(friendlyOverloads);
        Assert.All(
            friendlyOverloads,
            method =>
            {
                string documentation = method.GetLeadingTrivia().ToString();
                if (includeHiddenPCWSTR)
                {
                    Assert.Contains("<summary>", documentation);
                    Assert.Contains("<param name=\"", documentation);
                    Assert.DoesNotContain("<inheritdoc cref=", documentation);
                    if (methodName == "ConvertStringSecurityDescriptorToSecurityDescriptor" && method.ParameterList.Parameters.Count == 3)
                    {
                        Assert.DoesNotContain("<param name=\"SecurityDescriptorSize\">", documentation);
                    }
                }
                else
                {
                    Assert.Contains("<inheritdoc cref=\"", documentation);
                    Assert.Contains("winmdroot.Foundation.PCWSTR", documentation);
                }
            });

        if (includeHiddenPCWSTR && extensionReceiver)
        {
            INamedTypeSymbol pcwstr = Assert.IsAssignableFrom<INamedTypeSymbol>(referencedProject.GetTypeByMetadataName("Windows.Win32.Foundation.PCWSTR"));
            string pwstr = Assert.Single(this.FindGeneratedType("PWSTR")).ToFullString();
            Assert.Contains($"cref=\"{Assert.Single(pcwstr.GetMembers("Length")).GetDocumentationCommentId()}\"", pwstr);
            Assert.Contains($"cref=\"{Assert.Single(pcwstr.GetMembers("ToString")).GetDocumentationCommentId()}\"", pwstr);
        }
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
