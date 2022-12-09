// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class COMTests : GeneratorTestBase
{
    private const string WinRTCustomMarshalerClass = "WinRTCustomMarshaler";
    private const string WinRTCustomMarshalerNamespace = "Windows.Win32.CsWin32.InteropServices";
    private const string WinRTCustomMarshalerFullName = WinRTCustomMarshalerNamespace + "." + WinRTCustomMarshalerClass;

    public COMTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void FriendlyOverloadOfCOMInterfaceRemovesParameter()
    {
        const string ifaceName = "IEnumDebugPropertyInfo";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedMethod("Next"), m => m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[0].Modifiers.Any(SyntaxKind.ThisKeyword));
    }

    [Fact]
    public void IDispatchDerivedInterface()
    {
        const string ifaceName = "IInkRectangle";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Contains(this.FindGeneratedType(ifaceName), t => t.BaseList is null && t.AttributeLists.Any(al => al.Attributes.Any(a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "InterfaceType" } } && a.ArgumentList?.Arguments[0].Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: nameof(ComInterfaceType.InterfaceIsIDispatch) } } })));
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Fact]
    public void IInpectableDerivedInterface()
    {
        const string ifaceName = "IUserConsentVerifierInterop";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedType(ifaceName), t => t.BaseList is null && ((InterfaceDeclarationSyntax)t).Members.Count == 1 && t.AttributeLists.Any(al => al.Attributes.Any(a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "InterfaceType" } } && a.ArgumentList?.Arguments[0].Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: nameof(ComInterfaceType.InterfaceIsIInspectable) } } })));

        // Make sure the WinRT marshaler was not brought in
        Assert.Empty(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Theory, PairwiseData]
    public void COMPropertiesAreGeneratedAsInterfaceProperties(bool allowMarshaling)
    {
        const string ifaceName = "IADsClass";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        InterfaceDeclarationSyntax ifaceSyntax;
        if (allowMarshaling)
        {
            ifaceSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<InterfaceDeclarationSyntax>());
        }
        else
        {
            StructDeclarationSyntax structSyntax = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType(ifaceName));
            ifaceSyntax = Assert.Single(structSyntax.Members.OfType<InterfaceDeclarationSyntax>(), m => m.Identifier.ValueText == "Interface");
        }

        // Check a property where we expect just a getter.
        Assert.NotNull(FindAccessor(ifaceSyntax, "PrimaryInterface", SyntaxKind.GetAccessorDeclaration));
        Assert.Null(FindAccessor(ifaceSyntax, "PrimaryInterface", SyntaxKind.SetAccessorDeclaration));

        // Check a property where we expect both a getter and setter.
        Assert.NotNull(FindAccessor(ifaceSyntax, "CLSID", SyntaxKind.GetAccessorDeclaration));
        Assert.NotNull(FindAccessor(ifaceSyntax, "CLSID", SyntaxKind.SetAccessorDeclaration));
    }

    [Theory, PairwiseData]
    public void COMPropertiesAreGeneratedAsInterfaceProperties_NonConsecutiveAccessors(bool allowMarshaling)
    {
        const string ifaceName = "IUIAutomationProxyFactoryEntry";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        InterfaceDeclarationSyntax ifaceSyntax;
        if (allowMarshaling)
        {
            ifaceSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<InterfaceDeclarationSyntax>());
        }
        else
        {
            StructDeclarationSyntax structSyntax = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType(ifaceName));
            ifaceSyntax = Assert.Single(structSyntax.Members.OfType<InterfaceDeclarationSyntax>(), m => m.Identifier.ValueText == "Interface");
        }

        // Check for a property where the interface declares the getter and setter in non-consecutive rows of the VMT.
        Assert.Null(FindAccessor(ifaceSyntax, "ClassName", SyntaxKind.GetAccessorDeclaration));
        Assert.Null(FindAccessor(ifaceSyntax, "ClassName", SyntaxKind.SetAccessorDeclaration));
    }

    [Fact]
    public void COMPropertiesAreGeneratedAsStructProperties()
    {
        const string ifaceName = "IADsClass";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<StructDeclarationSyntax>());

        // Check a property where we expect just a getter.
        Assert.NotNull(FindAccessor(structSyntax, "PrimaryInterface", SyntaxKind.GetAccessorDeclaration));
        Assert.Null(FindAccessor(structSyntax, "PrimaryInterface", SyntaxKind.SetAccessorDeclaration));

        // Check a property where we expect both a getter and setter.
        Assert.NotNull(FindAccessor(structSyntax, "CLSID", SyntaxKind.GetAccessorDeclaration));
        Assert.NotNull(FindAccessor(structSyntax, "CLSID", SyntaxKind.SetAccessorDeclaration));
    }

    [Fact]
    public void COMPropertiesAreGeneratedAsStructProperties_NonConsecutiveAccessors()
    {
        const string ifaceName = "IUIAutomationProxyFactoryEntry";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<StructDeclarationSyntax>());

        // Check for a property where the interface declares the getter and setter in non-consecutive rows of the VMT.
        // For structs, we can still declare both as accessors because we implement them, provided they have the same type.
        Assert.NotNull(FindAccessor(structSyntax, "CanCheckBaseClass", SyntaxKind.GetAccessorDeclaration));
        Assert.NotNull(FindAccessor(structSyntax, "CanCheckBaseClass", SyntaxKind.SetAccessorDeclaration));

        // And in some cases, the types are *not* the same, so don't generate any property.
        Assert.Null(FindAccessor(structSyntax, "ClassName", SyntaxKind.GetAccessorDeclaration));
        Assert.Null(FindAccessor(structSyntax, "ClassName", SyntaxKind.SetAccessorDeclaration));
        Assert.NotEmpty(structSyntax.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "get_ClassName"));
        Assert.NotEmpty(structSyntax.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "put_ClassName"));
    }

    /// <summary>
    /// Verifies that IPicture can be generated.
    /// It is a special case because of <see href="https://github.com/microsoft/win32metadata/issues/1367">this metadata bug</see>.
    /// </summary>
    [Fact]
    public void COMPropertiesAreGeneratedAsStructProperties_NonConsecutiveAccessors_IPicture()
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("IPicture", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, PairwiseData]
    public void COMPropertiesAreGeneratedAsMethodsWhenTheirReturnTypesDiffer([CombinatorialValues(0, 1, 2)] int marshaling)
    {
        const string ifaceName = "IHTMLImgElement";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = marshaling > 0, ComInterop = new GeneratorOptions.ComInteropOptions { UseIntPtrForComOutPointers = marshaling == 1 } });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        InterfaceDeclarationSyntax ifaceSyntax;
        if (marshaling > 0)
        {
            ifaceSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<InterfaceDeclarationSyntax>());
        }
        else
        {
            StructDeclarationSyntax structSyntax = (StructDeclarationSyntax)Assert.Single(this.FindGeneratedType(ifaceName));
            ifaceSyntax = Assert.Single(structSyntax.Members.OfType<InterfaceDeclarationSyntax>(), m => m.Identifier.ValueText == "Interface");
        }

        if (marshaling == 1)
        {
            Assert.Empty(ifaceSyntax.Members.OfType<PropertyDeclarationSyntax>().Where(m => m.Identifier.ValueText == "border"));
            Assert.NotEmpty(ifaceSyntax.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "put_border"));
            Assert.NotEmpty(ifaceSyntax.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "get_border"));
        }
        else
        {
            Assert.NotNull(FindAccessor(ifaceSyntax, "border", SyntaxKind.GetAccessorDeclaration));
            Assert.NotNull(FindAccessor(ifaceSyntax, "border", SyntaxKind.SetAccessorDeclaration));
        }
    }

    [Fact]
    public void WinRTInterfaceDoesntBringInMarshalerIfParamNotObject()
    {
        const string WinRTInteropInterfaceName = "IGraphicsEffectD2D1Interop";

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(WinRTInteropInterfaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Make sure the WinRT marshaler was not brought in
        Assert.Empty(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void WinRTInterfaceWithWinRTOutObjectUsesMarshaler()
    {
        const string WinRTInteropInterfaceName = "ICompositorDesktopInterop";
        const string WinRTClassName = "Windows.UI.Composition.Desktop.DesktopWindowTarget";

        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(WinRTInteropInterfaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        InterfaceDeclarationSyntax interfaceDeclaration = (InterfaceDeclarationSyntax)Assert.Single(this.FindGeneratedType(WinRTInteropInterfaceName));
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)interfaceDeclaration.Members.First();
        ParameterSyntax lastParam = method.ParameterList.Parameters.Last();

        Assert.Equal($"global::{WinRTClassName}", lastParam.Type?.ToString());
        Assert.True(lastParam.Modifiers.Any(SyntaxKind.OutKeyword));

        AttributeSyntax marshalAsAttr = Assert.Single(FindAttribute(lastParam.AttributeLists, "MarshalAs"));

        Assert.True(marshalAsAttr.ArgumentList?.Arguments[0].ToString() == "UnmanagedType.CustomMarshaler");
        Assert.Single(marshalAsAttr.ArgumentList.Arguments.Where(arg => arg.ToString() == $"MarshalCookie = \"{WinRTClassName}\""));
        Assert.Single(marshalAsAttr.ArgumentList.Arguments.Where(arg => arg.ToString() == $"MarshalType = \"{WinRTCustomMarshalerFullName}\""));

        // Make sure the WinRT marshaler was brought in
        Assert.Single(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void MethodWithHRParameter()
    {
        this.compilation = this.starterCompilations["net6.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("IFileOpenDialog", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// A non-COM compliant interface (since it doesn't derive from IUnknown).
    /// </summary>
    [Fact]
    public void IVssCreateWriterMetadata()
    {
        this.compilation = this.starterCompilations["net6.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("IVssCreateWriterMetadata", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// An IInspectable-derived interface.
    /// </summary>
    [Fact]
    public void IProtectionPolicyManagerInterop3()
    {
        this.compilation = this.starterCompilations["net6.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("IProtectionPolicyManagerInterop3", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void ComOutPtrTypedAsOutObject()
    {
        const string methodName = "CoCreateInstance";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedMethod(methodName), m => m.ParameterList.Parameters.Last() is { } last && last.Modifiers.Any(SyntaxKind.OutKeyword) && last.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ObjectKeyword } });
    }

    [Fact]
    public void ComOutPtrTypedAsIntPtr()
    {
        const string methodName = "CoCreateInstance";
        this.generator = this.CreateGenerator(new GeneratorOptions { ComInterop = new() { UseIntPtrForComOutPointers = true } });
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Contains(this.FindGeneratedMethod(methodName), m => m.ParameterList.Parameters.Last() is { } last && last.Modifiers.Any(SyntaxKind.OutKeyword) && last.Type is IdentifierNameSyntax { Identifier: { ValueText: "IntPtr" } });
    }

    [Theory, PairwiseData]
    public void NonCOMInterfaceReferences(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        const string methodName = "D3DCompile"; // A method whose signature references non-COM interface ID3DInclude
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // The generated methods MUST reference the "interface" (which must actually be generated as a struct) by pointer.
        Assert.Contains(this.FindGeneratedType("ID3DInclude"), t => t is StructDeclarationSyntax);
        Assert.All(this.FindGeneratedMethod(methodName), m => Assert.True(m.ParameterList.Parameters[4].Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "ID3DInclude" } } } }));
    }

    [Theory, PairwiseData]
    public void COMInterfaceWithSupportedOSPlatform(bool net60, bool allowMarshaling)
    {
        this.compilation = this.starterCompilations[net60 ? "net6.0" : "netstandard2.0"];
        const string typeName = "IInkCursors";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerateType(typeName));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var iface = this.FindGeneratedType(typeName).Single();

        if (net60)
        {
            Assert.Contains(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
        else
        {
            Assert.DoesNotContain(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
    }

    [Theory]
    [CombinatorialData]
    public void COMInterfaceIIDInterfaceOnAppropriateTFMs(
        bool allowMarshaling,
        [CombinatorialValues(LanguageVersion.CSharp10, LanguageVersion.CSharp11)] LanguageVersion langVersion,
        [CombinatorialValues("net6.0", "net7.0")] string tfm)
    {
        const string structName = "IEnumBstr";
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(langVersion);
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate(structName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        BaseTypeDeclarationSyntax type = this.FindGeneratedType(structName).Single();
        IEnumerable<BaseTypeSyntax> actual = type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>();
        Predicate<BaseTypeSyntax> predicate = t => t.Type.ToString().Contains("IComIID");

        // Static interface members requires C# 11 and .NET 7.
        // And COM *interfaces* are not allowed to have them, so assert we only generate them on structs.
        if (tfm == "net7.0" && langVersion >= LanguageVersion.CSharp11 && type is StructDeclarationSyntax)
        {
            Assert.Contains(actual, predicate);
        }
        else
        {
            Assert.DoesNotContain(actual, predicate);
        }
    }
}
