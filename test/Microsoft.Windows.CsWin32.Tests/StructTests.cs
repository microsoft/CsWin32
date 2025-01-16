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
        this.compilation = this.starterCompilations["net8.0-x64"]; // MEMORY_BASIC_INFORMATION is arch-specific
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
        this.compilation = this.compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(test, this.parseOptions, "test.cs", cancellationToken: TestContext.Current.CancellationToken));
        this.GenerateApi("CreateFile");
    }

    [Fact]
    public void PartialStructsAllowUserContributions()
    {
        const string structName = "HRESULT";
        this.compilation = this.compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Microsoft.Windows.Sdk { partial struct HRESULT { void Foo() { } } }", this.parseOptions, "myHRESULT.cs", cancellationToken: TestContext.Current.CancellationToken));

        this.GenerateApi(structName);

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
        this.GenerateApi("PROC");

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("PROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void FARPROC_GeneratedAsStruct(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.GenerateApi("FARPROC");

        BaseTypeDeclarationSyntax type = Assert.Single(this.FindGeneratedType("FARPROC"));
        Assert.IsType<StructDeclarationSyntax>(type);
    }

    [Fact]
    public void PointerFieldIsDeclaredAsPointer()
    {
        this.GenerateApi("PROCESS_BASIC_INFORMATION");

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("PROCESS_BASIC_INFORMATION"));
        FieldDeclarationSyntax field = Assert.Single(type.Members.OfType<FieldDeclarationSyntax>(), m => m.Declaration.Variables.Any(v => v.Identifier.ValueText == "PebBaseAddress"));
        Assert.IsType<PointerTypeSyntax>(field.Declaration.Type);
    }

    [Fact]
    public void FieldWithAssociatedEnum()
    {
        this.GenerateApi("SHDESCRIPTIONID");

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("SHDESCRIPTIONID"));
        PropertyDeclarationSyntax property = Assert.Single(type.Members.OfType<PropertyDeclarationSyntax>(), m => m.Identifier.ValueText == "dwDescriptionId");
        Assert.Equal("SHDID_ID", Assert.IsType<IdentifierNameSyntax>(property.Type).Identifier.ValueText);
    }

    [Fact]
    public void Bitfield_Bool()
    {
        this.GenerateApi("SHELLFLAGSTATE");
        StructDeclarationSyntax structDecl = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("SHELLFLAGSTATE"));

        // Verify that the struct has a single field of type int.
        FieldDeclarationSyntax bitfield = Assert.Single(structDecl.Members.OfType<FieldDeclarationSyntax>());
        Assert.True(bitfield.Declaration.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } });

        // Verify that many other *properties* are added that access into the bitfield.
        // The actual behavior of the properties is verified in the functional unit tests.
        List<PropertyDeclarationSyntax> properties = structDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        Assert.Contains(properties, p => p.Identifier.ValueText == "fShowAllObjects" && p.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.BoolKeyword } });
        Assert.Contains(properties, p => p.Identifier.ValueText == "fShowExtensions" && p.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.BoolKeyword } });
    }

    [Fact]
    public void Bitfield_UIntPtr()
    {
        this.GenerateApi("PSAPI_WORKING_SET_BLOCK");
        StructDeclarationSyntax structDecl = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("_Anonymous_e__Struct"));

        // Verify that the struct has a single field of type int.
        FieldDeclarationSyntax bitfield = Assert.Single(structDecl.Members.OfType<FieldDeclarationSyntax>());
        Assert.True(bitfield.Declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "nuint" });

        // Verify that many other *properties* are added that access into the bitfield.
        // The actual behavior of the properties is verified in the functional unit tests.
        List<PropertyDeclarationSyntax> properties = structDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        Assert.Contains(properties, p => p.Identifier.ValueText == "Protection" && p.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ByteKeyword } });
        Assert.Contains(properties, p => p.Identifier.ValueText == "Shared" && p.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.BoolKeyword } });
    }

    [Fact]
    public void Bitfield_Multiple()
    {
        this.GenerateApi("AM_COLCON");
        StructDeclarationSyntax structDecl = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("AM_COLCON"));
        Assert.Equal(4, structDecl.Members.OfType<FieldDeclarationSyntax>().Count());

        // Verify that each field produced 2 properties of type byte.
        List<PropertyDeclarationSyntax> properties = structDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        Assert.Equal(4 * 2, properties.Count);
        Assert.All(properties, p => Assert.True(p.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ByteKeyword } }));
        Assert.Contains(properties, p => p.Identifier.ValueText == "emph1col");
        Assert.Contains(properties, p => p.Identifier.ValueText == "patcon");
    }

    [Fact]
    public void Bitfield_MultiplePropertyTypes()
    {
        this.GenerateApi("BM_REQUEST_TYPE");
        StructDeclarationSyntax structDecl = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("_BM"));

        List<PropertyDeclarationSyntax> fields = structDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        Assert.Equal(4, fields.Count);
        Assert.Equal(SyntaxKind.ByteKeyword, GetPropertyType("Recipient"));
        Assert.Equal(SyntaxKind.ByteKeyword, GetPropertyType("Reserved"));
        Assert.Equal(SyntaxKind.ByteKeyword, GetPropertyType("Type"));
        Assert.Equal(SyntaxKind.BoolKeyword, GetPropertyType("Dir"));

        SyntaxKind GetPropertyType(string name) => ((PredefinedTypeSyntax)fields.Single(f => f.Identifier.ValueText == name).Type).Keyword.Kind();
    }

    [Theory]
    [InlineData("PCSTR")]
    public void SpecialStruct_ByRequest(string structName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(structName, out IReadOnlyCollection<string> preciseApi, CancellationToken.None));
        Assert.Single(preciseApi);
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType(structName));
    }

    [Fact]
    public void StructConstantsAreGeneratedAsConstants()
    {
        this.GenerateApi("Color");
        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("Color"));
        FieldDeclarationSyntax argb = Assert.Single(type.Members.OfType<FieldDeclarationSyntax>(), f => !(f.Modifiers.Any(SyntaxKind.StaticKeyword) || f.Modifiers.Any(SyntaxKind.ConstKeyword)));
        Assert.Equal("Argb", argb.Declaration.Variables.Single().Identifier.ValueText);
        Assert.Contains(type.Members.OfType<FieldDeclarationSyntax>(), f => f.Modifiers.Any(SyntaxKind.ConstKeyword));
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void FlexibleArrayMember(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.GenerateApi("BITMAPINFO");
        var type = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType("BITMAPINFO"));
        FieldDeclarationSyntax flexArrayField = Assert.Single(type.Members.OfType<FieldDeclarationSyntax>(), m => m.Declaration.Variables.Any(v => v.Identifier.ValueText == "bmiColors"));
        var fieldType = Assert.IsType<GenericNameSyntax>(Assert.IsType<QualifiedNameSyntax>(flexArrayField.Declaration.Type).Right);
        Assert.Equal("VariableLengthInlineArray", fieldType.Identifier.ValueText);
        Assert.Equal("RGBQUAD", Assert.IsType<QualifiedNameSyntax>(Assert.Single(fieldType.TypeArgumentList.Arguments)).Right.Identifier.ValueText);

        // Verify that the SizeOf method was generated.
        Assert.Single(this.FindGeneratedMethod("SizeOf"));
    }

    [Theory]
    [CombinatorialData]
    public void InterestingStructs(
        [CombinatorialValues(
        "VARIANT_BOOL", // has a custom conversion to bool and relies on other members being generated
        "DRIVER_OBJECT", // has an inline array of delegates
        "DEVICE_RELATIONS", // ends with an inline "flexible" array
        "D3DHAL_CONTEXTCREATEDATA", // contains a field that is a pointer to a struct that is normally managed
        "MIB_TCPTABLE", // a struct that references another struct with a nested anonymous type, that loosely references an enum in the same namespace (by way of an attribute).
        "WHEA_XPF_TLB_CHECK", // a struct with a ulong bitfield with one field exceeding 32-bits in length.
        "TRANSPORT_PROPERTIES", // a struct with a long bitfield with one subfield expressed as ulong.
        "D3DKMDT_DISPLAYMODE_FLAGS", // a struct with an interesting bool/byte conversion.
        "WSD_EVENT")] // has a pointer field to a managed struct
        string name,
        bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with
        {
            AllowMarshaling = allowMarshaling,
        };
        this.generator = this.CreateGenerator(options);
        this.GenerateApi(name);
    }
}
