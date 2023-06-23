// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class StructTests : GeneratorTestBase
{
    public StructTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void CocreatableStructs()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("ShellLink", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        ClassDeclarationSyntax classDecl = Assert.IsType<ClassDeclarationSyntax>(this.FindGeneratedType("ShellLink").Single());
        Assert.Contains(classDecl.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("ComImport")));
    }

    [Theory]
    [InlineData("BCRYPT_KEY_HANDLE", "BCRYPT_HANDLE")]
    public void AlsoUsableForTypeDefs(string structName, string alsoUsableForStructName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(structName).Single());
        Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(alsoUsableForStructName).Single());
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("HRESULT")]
    [InlineData("MEMORY_BASIC_INFORMATION")]
    public void StructsArePartial(string structName)
    {
        this.compilation = this.starterCompilations["net6.0-x64"]; // MEMORY_BASIC_INFORMATION is arch-specific
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structDecl = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(structName).Single());
        Assert.True(structDecl.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void PointReferencingStruct(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("INPUTCONTEXT", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, CombinatorialData]
    public void RECT_IncludesSystemDrawingWhenReferenced(bool referenceSystemDrawing)
    {
        this.compilation = this.starterCompilations["net472"];
        if (!referenceSystemDrawing)
        {
            this.compilation = this.compilation.RemoveReferences(this.compilation.References.Where(r => r.Display?.EndsWith("System.Drawing.dll", StringComparison.OrdinalIgnoreCase) is true));
        }

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("RECT", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        StructDeclarationSyntax rectStruct = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("RECT"));
        Assert.Equal(referenceSystemDrawing, rectStruct.Members.OfType<ConstructorDeclarationSyntax>().Any(ctor => ctor.ParameterList.Parameters.Any(p => p.Type?.ToString().Contains("System.Drawing.Rectangle") is true)));
    }

    [Fact]
    public void CollidingStructNotGenerated()
    {
        const string test = @"
namespace Microsoft.Windows.Sdk
{
    internal enum FILE_CREATE_FLAGS
    {
        CREATE_NEW = 1,
        CREATE_ALWAYS = 2,
        OPEN_EXISTING = 3,
        OPEN_ALWAYS = 4,
        TRUNCATE_EXISTING = 5,
    }
}
";
        this.compilation = this.compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(test, this.parseOptions, "test.cs"));
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreateFile", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void PartialStructsAllowUserContributions()
    {
        const string structName = "HRESULT";
        this.compilation = this.compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.Windows.Sdk { partial struct HRESULT { void Foo() { } } }", this.parseOptions, "myHRESULT.cs"));

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        bool hasFooMethod = false;
        bool hasValueProperty = false;
        foreach (StructDeclarationSyntax structDecl in this.FindGeneratedType(structName))
        {
            hasFooMethod |= structDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "Foo");
            hasValueProperty |= structDecl.Members.OfType<FieldDeclarationSyntax>().Any(p => p.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText == "Value");
        }

        Assert.True(hasFooMethod, "User-defined method not found.");
        Assert.True(hasValueProperty, "Projected members not found.");
    }

    [Fact]
    public void PROC_GeneratedAsStruct()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("PROC", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("PROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void FARPROC_GeneratedAsStruct(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("FARPROC", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("FARPROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Fact]
    public void PointerFieldIsDeclaredAsPointer()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("PROCESS_BASIC_INFORMATION", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("PROCESS_BASIC_INFORMATION"));
        FieldDeclarationSyntax field = Assert.Single(type.Members.OfType<FieldDeclarationSyntax>(), m => m.Declaration.Variables.Any(v => v.Identifier.ValueText == "PebBaseAddress"));
        Assert.IsType<PointerTypeSyntax>(field.Declaration.Type);
    }

    [Fact]
    public void FieldWithAssociatedEnum()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("SHDESCRIPTIONID", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("SHDESCRIPTIONID"));
        PropertyDeclarationSyntax property = Assert.Single(type.Members.OfType<PropertyDeclarationSyntax>(), m => m.Identifier.ValueText == "dwDescriptionId");
        Assert.Equal("SHDID_ID", Assert.IsType<IdentifierNameSyntax>(property.Type).Identifier.ValueText);
    }

    [Theory]
    [InlineData("PCSTR")]
    public void SpecialStruct_ByRequest(string structName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, out IReadOnlyList<string> preciseApi, CancellationToken.None));
        Assert.Single(preciseApi);
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType(structName));
    }

    [Theory]
    [CombinatorialData]
    public void InterestingStructs(
        [CombinatorialValues(
        "DRIVER_OBJECT", // has an inline array of delegates
        "DEVICE_RELATIONS", // ends with an inline "flexible" array
        "D3DHAL_CONTEXTCREATEDATA", // contains a field that is a pointer to a struct that is normally managed
        "WSD_EVENT")] // has a pointer field to a managed struct
        string name,
        bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with
        {
            AllowMarshaling = allowMarshaling,
        };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate(name, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }
}
