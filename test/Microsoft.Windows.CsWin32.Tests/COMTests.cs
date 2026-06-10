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
        this.GenerateApi(ifaceName);
        Assert.Contains(this.FindGeneratedMethod("Next"), m => m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[0].Modifiers.Any(SyntaxKind.ThisKeyword));
    }

    [Theory]
    [InlineData("IHTMLDocument")]
    [InlineData("IHTMLDocument2")]
    public void IDispatchInterfaceIsDual(string ifaceName)
    {
        // Interfaces that derive from IDispatch are also IUnknown.
        // We should label them as such iff members are defined on the interface (beyond those defined on IDispatch).
        this.GenerateApi(ifaceName);
        InterfaceDeclarationSyntax iface = (InterfaceDeclarationSyntax)this.FindGeneratedType(ifaceName).Single();
        AttributeSyntax ifaceAttr = iface.AttributeLists.SelectMany(al => al.Attributes).Single(att => att.Name.ToString() == "InterfaceType");
        var arg = Assert.IsType<MemberAccessExpressionSyntax>(ifaceAttr.ArgumentList?.Arguments[0].Expression);
        Assert.Equal(nameof(ComInterfaceType.InterfaceIsDual), arg.Name.Identifier.ValueText);
    }

    [Fact]
    public void CreateDispatcherQueueController_CreatesWinRTCustomMarshaler()
    {
        this.GenerateApi("CreateDispatcherQueueController");
        Assert.Single(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void IGraphicsEffectD2D1Interop_ProjectsIPropertyValueParameter()
    {
        this.GenerateApi("IGraphicsEffectD2D1Interop");
        InterfaceDeclarationSyntax iface = (InterfaceDeclarationSyntax)this.FindGeneratedType("IGraphicsEffectD2D1Interop").Single();
        MethodDeclarationSyntax getPropertyMember = (MethodDeclarationSyntax)iface.Members[3];
        ParameterSyntax iPropertyValueParameter = getPropertyMember.ParameterList.Parameters[1];
        Assert.Equal("global::Windows.Foundation.IPropertyValue", iPropertyValueParameter.Type?.ToString());
    }

    [Fact]
    public void IInpectableDerivedInterface()
    {
        const string ifaceName = "IUserConsentVerifierInterop";
        this.GenerateApi(ifaceName);
        Assert.Contains(this.FindGeneratedType(ifaceName), t => t.BaseList is null && ((InterfaceDeclarationSyntax)t).Members.Count == 1 && t.AttributeLists.Any(al => al.Attributes.Any(a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "InterfaceType" } } && a.ArgumentList?.Arguments[0].Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: nameof(ComInterfaceType.InterfaceIsIInspectable) } } })));

        // Make sure the WinRT marshaler was not brought in
        Assert.Empty(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Theory, PairwiseData]
    public void COMPropertiesAreGeneratedAsInterfaceProperties(bool allowMarshaling)
    {
        const string ifaceName = "IADsClass";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi(ifaceName);
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
        this.GenerateApi(ifaceName);
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
        this.GenerateApi(ifaceName);
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
        this.GenerateApi(ifaceName);
        StructDeclarationSyntax structSyntax = Assert.Single(this.FindGeneratedType(ifaceName).OfType<StructDeclarationSyntax>());

        // Check for a property where the interface declares the getter and setter in non-consecutive rows of the VMT.
        // For structs, we can still declare both as accessors because we implement them, provided they have the same type.
        Assert.NotNull(FindAccessor(structSyntax, "CanCheckBaseClass", SyntaxKind.GetAccessorDeclaration));
        Assert.NotNull(FindAccessor(structSyntax, "CanCheckBaseClass", SyntaxKind.SetAccessorDeclaration));

        // And in some cases, the types are *not* the same, so don't generate any property.
        Assert.Null(FindAccessor(structSyntax, "ClassName", SyntaxKind.GetAccessorDeclaration));
        Assert.Null(FindAccessor(structSyntax, "ClassName", SyntaxKind.SetAccessorDeclaration));
        Assert.Contains(structSyntax.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "get_ClassName");
        Assert.Contains(structSyntax.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "put_ClassName");
    }

    /// <summary>
    /// Verifies that IPicture can be generated.
    /// It is a special case because of <see href="https://github.com/microsoft/win32metadata/issues/1367">this metadata bug</see>.
    /// </summary>
    [Fact]
    public void COMPropertiesAreGeneratedAsStructProperties_NonConsecutiveAccessors_IPicture()
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi("IPicture");
    }

    [Theory, PairwiseData]
    public void COMPropertiesAreGeneratedAsMethodsWhenTheirReturnTypesDiffer([CombinatorialValues(0, 1, 2)] int marshaling)
    {
        const string ifaceName = "IHTMLImgElement";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = marshaling > 0, ComInterop = new GeneratorOptions.ComInteropOptions { UseIntPtrForComOutPointers = marshaling == 1 } });
        this.GenerateApi(ifaceName);
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
            Assert.DoesNotContain(ifaceSyntax.Members.OfType<PropertyDeclarationSyntax>(), m => m.Identifier.ValueText == "border");
            Assert.Contains(ifaceSyntax.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "put_border");
            Assert.Contains(ifaceSyntax.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "get_border");
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

        this.GenerateApi(WinRTInteropInterfaceName);

        InterfaceDeclarationSyntax interfaceDeclaration = (InterfaceDeclarationSyntax)Assert.Single(this.FindGeneratedType(WinRTInteropInterfaceName));
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)interfaceDeclaration.Members.First();
        ParameterSyntax lastParam = method.ParameterList.Parameters.Last();

        Assert.Equal($"global::{WinRTClassName}", lastParam.Type?.ToString());
        Assert.True(lastParam.Modifiers.Any(SyntaxKind.OutKeyword));

        AttributeSyntax marshalAsAttr = Assert.Single(FindAttribute(lastParam.AttributeLists, "MarshalAs"));

        Assert.True(marshalAsAttr.ArgumentList?.Arguments[0].ToString() == "UnmanagedType.CustomMarshaler");
        Assert.Single(marshalAsAttr.ArgumentList.Arguments, arg => arg.ToString() == $"MarshalCookie = \"{WinRTClassName}\"");
        Assert.Single(marshalAsAttr.ArgumentList.Arguments, arg => arg.ToString() == $"MarshalType = \"{WinRTCustomMarshalerFullName}\"");

        // Make sure the WinRT marshaler was brought in
        Assert.Single(this.FindGeneratedType(WinRTCustomMarshalerClass));
    }

    [Fact]
    public void MethodWithHRParameter()
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        Assert.True(this.generator.TryGenerate("IFileOpenDialog", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void AssociatedEnumOnMethodParameters()
    {
        this.GenerateApi("IShellFolderView");

        InterfaceDeclarationSyntax ifaceSyntax = Assert.Single(this.FindGeneratedType("IShellFolderView").OfType<InterfaceDeclarationSyntax>());
        MethodDeclarationSyntax methodSyntax = Assert.Single(ifaceSyntax.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "Select");
        ParameterSyntax parameter = Assert.Single(methodSyntax.ParameterList.Parameters);
        Assert.Equal("SFVS_SELECT", Assert.IsType<QualifiedNameSyntax>(parameter.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void IShellFolderViewDual_ApplicationPropertyHasMarshalAsAttribute()
    {
        this.GenerateApi("IShellFolderViewDual");

        InterfaceDeclarationSyntax ifaceSyntax = Assert.Single(this.FindGeneratedType("IShellFolderViewDual").OfType<InterfaceDeclarationSyntax>());
        PropertyDeclarationSyntax applicationProperty = Assert.Single(ifaceSyntax.Members.OfType<PropertyDeclarationSyntax>(), p => p.Identifier.ValueText == "Application");
        AccessorDeclarationSyntax getAccessor = Assert.Single(applicationProperty.AccessorList!.Accessors, a => a.Kind() == SyntaxKind.GetAccessorDeclaration);

        // Check that the return attribute list has MarshalAs attribute
        AttributeSyntax marshalAsAttr = Assert.Single(FindAttribute(getAccessor.AttributeLists, "MarshalAs"));

        // Verify it's using the correct UnmanagedType for IDispatch
        Assert.NotNull(marshalAsAttr.ArgumentList);
        Assert.Contains(marshalAsAttr.ArgumentList.Arguments, arg =>
            arg.Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { ValueText: "IDispatch" } } });
    }

    [Theory, CombinatorialData]
    public void InterestingUnmarshaledComInterfaces(
        [CombinatorialValues(
        "IUnknown",
        "IDispatch",
        "IInspectable")]
        string api,
        [CombinatorialMemberData(nameof(TFMDataNoNetFx35))]
        string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi(api);
    }

    [Theory]
    [CombinatorialData]
    public void InterestingComInterfaces(
        [CombinatorialValues(
            "IVssCreateWriterMetadata", // A non-COM compliant interface (since it doesn't derive from IUnknown).
            "IProtectionPolicyManagerInterop3", // An IInspectable-derived interface.
            "ICompositionCapabilitiesInteropFactory", // An interface with managed types.
            "IPicture", // An interface with properties that cannot be represented as properties.
            "ID2D1DeviceContext2", // CreateLookupTable3D takes fixed length arrays as parameters
            "IVPBaseConfig", // GetConnectInfo has a CountParamIndex that points to an [In, Out] parameter.
            "IXAudio2SourceVoice", // Requires switch to unmanaged IXAudio2Voice struct which verifies type names retain the _unmanaged suffix everywhere required.
            "MSP_EVENT_INFO", // Generates ITStream_unmanaged and ITTerminal_unmanaged
            "IWMDMDevice2")] // The GetSpecifyPropertyPages method has an NativeArrayInfo.CountParamIndex pointing at an [Out] parameter.
        string api,
        bool allowMarshaling)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi(api);
    }

    /// <summary>
    /// Verifies that COM methods that accept `[Optional, In]` parameters are declared as pointers
    /// rather than `in` parameters, since the marshaller will throw NRE if the reference is null (via <see cref="Unsafe.NullRef{T}"/>).
    /// </summary>
    /// <seealso href="https://github.com/microsoft/CsWin32/issues/1081"/>
    [Fact]
    public void OptionalInPointerParameterExposedAsPointer()
    {
        this.GenerateApi("IMMDevice");

        MethodDeclarationSyntax comMethod = this.FindGeneratedMethod("Activate").First(m => !m.Modifiers.Any(SyntaxKind.StaticKeyword));
        ParameterSyntax optionalInParam = comMethod.ParameterList.Parameters[2];
        Assert.Empty(optionalInParam.Modifiers);
        Assert.IsType<PointerTypeSyntax>(optionalInParam.Type);
    }

    [Fact]
    public void EnvironmentFailFast()
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });

        // Emit something into the Environment namespace, to invite collisions.
        Assert.True(this.generator.TryGenerate("ENCLAVE_IDENTITY", CancellationToken.None));

        // Emit the interface that can require Environment.FailFast.
        Assert.True(this.generator.TryGenerate("ITypeInfo", CancellationToken.None));

        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void ComOutPtrTypedAsOutObject()
    {
        const string methodName = "CoCreateInstance";
        this.GenerateApi(methodName);
        Assert.Contains(this.FindGeneratedMethod(methodName), m => m.ParameterList.Parameters.Last() is { } last && last.Modifiers.Any(SyntaxKind.OutKeyword) && last.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.ObjectKeyword } });
    }

    [Fact]
    public void ComOutPtrTypedAsIntPtr()
    {
        const string methodName = "CoCreateInstance";
        this.generator = this.CreateGenerator(new GeneratorOptions { ComInterop = new() { UseIntPtrForComOutPointers = true } });
        this.GenerateApi(methodName);
        Assert.Contains(this.FindGeneratedMethod(methodName), m => m.ParameterList.Parameters.Last() is { } last && last.Modifiers.Any(SyntaxKind.OutKeyword) && last.Type is IdentifierNameSyntax { Identifier: { ValueText: "IntPtr" } });
    }

    [Theory, PairwiseData]
    public void NonCOMInterfaceReferences(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        const string methodName = "D3DCompile"; // A method whose signature references non-COM interface ID3DInclude
        this.GenerateApi(methodName);

        // The generated methods MUST reference the "interface" (which must actually be generated as a struct) by pointer.
        Assert.Contains(this.FindGeneratedType("ID3DInclude"), t => t is StructDeclarationSyntax);
        Assert.All(this.FindGeneratedMethod(methodName), m =>
        {
            var parameter = m.ParameterList.Parameters.First(x => x.Identifier.ValueText == "pInclude");
            Assert.True(parameter.Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "ID3DInclude" } } } });
        });
    }

    [Theory, PairwiseData]
    public void COMInterfaceWithSupportedOSPlatform(bool netstandard, bool allowMarshaling)
    {
        this.compilation = this.starterCompilations[netstandard ? "netstandard2.0" : "net8.0"];
        const string typeName = "IInkCursors";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi(typeName);

        var iface = this.FindGeneratedType(typeName).Single();

        if (netstandard)
        {
            Assert.DoesNotContain(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
        else
        {
            Assert.Contains(iface.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
    }

    [Fact]
    public void IStream_ProducesPopulateVTable()
    {
        this.compilation = this.starterCompilations["net8.0"];
        const string typeName = "IStream";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi(typeName);
        var iface = (StructDeclarationSyntax)this.FindGeneratedType(typeName).Single();
        Assert.Single(iface.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "PopulateVTable");
    }

    [Fact]
    public void IPersistFile_DerivesFromIComIID()
    {
        this.compilation = this.starterCompilations["net8.0"];
        const string typeName = "IPersistFile";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi(typeName);
        var iface = (StructDeclarationSyntax)this.FindGeneratedType(typeName).Single();
        Assert.NotNull(iface.BaseList);
        Assert.Single(iface.BaseList.Types, bt => bt.Type.ToString().Contains("IComIID"));
    }

    [Theory, PairwiseData]
    public void ITypeNameBuilder_ToStringOverload(bool allowMarshaling)
    {
        const string typeName = "ITypeNameBuilder";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi(typeName);
    }

    [Fact]
    public void ReferencesToStructWithFlexibleArrayAreAlwaysPointers()
    {
        this.GenerateApi("IAMLine21Decoder");
        Assert.All(this.FindGeneratedMethod("SetOutputFormat"), m => Assert.IsType<PointerTypeSyntax>(m.ParameterList.Parameters[0].Type));

        // Assert that the 'unmanaged' declaration of the struct is the *only* declaration.
        Assert.Single(this.FindGeneratedType("BITMAPINFO"));
        Assert.Empty(this.FindGeneratedType("BITMAPINFO_unmanaged"));
    }

    [Theory]
    [CombinatorialData]
    public void COMInterfaceIIDInterfaceOnAppropriateTFMs(
        bool allowMarshaling,
        [CombinatorialValues("net8.0", "net9.0")] string tfm)
    {
        const string structName = "IEnumBstr";
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(GetLanguageVersionForTfm(tfm) ?? LanguageVersion.Latest);
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi(structName);

        BaseTypeDeclarationSyntax type = this.FindGeneratedType(structName).Single();
        IEnumerable<BaseTypeSyntax> actual = type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>();
        Predicate<BaseTypeSyntax> predicate = t => t.Type.ToString().Contains("IComIID");

        // Static interface members requires C# 11 and .NET 7+.
        // And COM *interfaces* are not allowed to have them, so assert we only generate them on structs.
        if (this.parseOptions.LanguageVersion >= LanguageVersion.CSharp11 && type is StructDeclarationSyntax)
        {
            Assert.Contains(actual, predicate);
        }
        else
        {
            Assert.DoesNotContain(actual, predicate);
        }
    }

    /// <summary>
    /// Regression test for <see href="https://github.com/microsoft/CsWin32/issues/1704">issue 1704</see>:
    /// the <c>IComIID</c> interface (and its attachment on generated COM structs) must also be emitted
    /// on target frameworks that do not support static abstract interface members
    /// (<c>net472</c>, <c>netstandard2.0</c>), using an instance-form property.
    /// </summary>
    [Theory]
    [CombinatorialData]
    public void COMInterfaceIIDInterfaceOnDownlevelTFMs(
        [CombinatorialValues("net472", "netstandard2.0")] string tfm)
    {
        const string structName = "IEnumBstr";
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });
        this.GenerateApi(structName);

        BaseTypeDeclarationSyntax type = this.FindGeneratedType(structName).Single();
        Assert.IsType<StructDeclarationSyntax>(type);
        IEnumerable<BaseTypeSyntax> baseTypes = type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>();
        Assert.Contains(baseTypes, t => t.Type.ToString().Contains("IComIID"));

        // The IComIID interface itself must be generated.
        InterfaceDeclarationSyntax icomIidDecl = Assert.IsType<InterfaceDeclarationSyntax>(this.FindGeneratedType("IComIID").Single());

        // Its Guid property must be instance-form (no 'static' modifier) on these TFMs,
        // and must be `ref readonly Guid` to match the dotnet/winforms polyfill shape.
        PropertyDeclarationSyntax guidProp = icomIidDecl.Members.OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "Guid");
        Assert.DoesNotContain(guidProp.Modifiers, m => m.IsKind(SyntaxKind.StaticKeyword));
        Assert.DoesNotContain(guidProp.Modifiers, m => m.IsKind(SyntaxKind.AbstractKeyword));
        string guidPropText = guidProp.NormalizeWhitespace().ToFullString();
        Assert.Contains("ref readonly Guid Guid", guidPropText);

        // And the struct must contain an explicit interface implementation of IComIID.Guid,
        // also as `ref readonly Guid` (matching the WinForms `ref Unsafe.AsRef(in IID_Guid)` pattern).
        var explicitImpl = ((StructDeclarationSyntax)type).Members.OfType<PropertyDeclarationSyntax>()
            .SingleOrDefault(p => p.ExplicitInterfaceSpecifier is not null && p.Identifier.ValueText == "Guid");
        Assert.NotNull(explicitImpl);
        Assert.DoesNotContain(explicitImpl!.Modifiers, m => m.IsKind(SyntaxKind.StaticKeyword));
        string implText = explicitImpl.NormalizeWhitespace().ToFullString();
        Assert.Contains("ref readonly Guid IComIID.Guid", implText);
        Assert.Contains("Unsafe.AsRef(in IID_Guid)", implText);
    }

    /// <summary>
    /// Multi-targeting regression test for <see href="https://github.com/microsoft/CsWin32/issues/1704">issue 1704</see>.
    /// Mirrors the issue's repro project (TargetFrameworks = net10.0;net472 with the exact
    /// NativeMethods.txt content) and asserts that <c>IComIID</c> is generated, attached, and
    /// usable as a generic type constraint on every TFM a typical multi-targeted project
    /// might build for — not only those that support static abstract interface members.
    /// </summary>
    [Theory]
    [CombinatorialData]
    public void IComIID_MultiTargeting_Issue1704(
        [CombinatorialValues("net472", "netstandard2.0", "net8.0", "net9.0", "net10.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(GetLanguageVersionForTfm(tfm) ?? LanguageVersion.Latest);
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });

        // The exact NativeMethods.txt content from the issue repro.
        Assert.True(this.generator.TryGenerate("GetRunningObjectTable", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("IRunningObjectTable", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("IMoniker", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("CoCreateInstance", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // 1. The IComIID interface itself must be generated on every TFM.
        InterfaceDeclarationSyntax icomIidDecl = Assert.IsType<InterfaceDeclarationSyntax>(this.FindGeneratedType("IComIID").Single());

        // 2. Every generated COM struct must list IComIID in its base list.
        foreach (string structName in new[] { "IRunningObjectTable", "IMoniker" })
        {
            BaseTypeDeclarationSyntax type = this.FindGeneratedType(structName).Single();
            Assert.IsType<StructDeclarationSyntax>(type);
            IEnumerable<BaseTypeSyntax> baseTypes = type.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>();
            Assert.Contains(baseTypes, t => t.Type.ToString().Contains("IComIID"));
        }

        // 3. Consumer code that uses IComIID as a generic constraint must compile on every TFM.
        //    Use the WinForms-style `ref readonly Guid` pattern (see dotnet/winforms IDataObject.cs)
        //    which is uniform across both static-abstract and downlevel forms — only the call site differs.
        bool hasStaticAbstract = this.parseOptions.LanguageVersion >= LanguageVersion.CSharp11
            && (tfm.StartsWith("net8", StringComparison.Ordinal)
                || tfm.StartsWith("net9", StringComparison.Ordinal)
                || tfm.StartsWith("net10", StringComparison.Ordinal));

        string consumerSnippet = hasStaticAbstract
            ? """
                using System;
                using Windows.Win32;
                using Windows.Win32.System.Com;

                internal static unsafe class Issue1704Consumer
                {
                    private static ref readonly Guid GetIID<T>() where T : unmanaged, IComIID => ref T.Guid;
                    public static Guid M() => GetIID<IRunningObjectTable>();
                }
                """
            : """
                using System;
                using Windows.Win32;
                using Windows.Win32.System.Com;

                internal static unsafe class Issue1704Consumer
                {
                    private static ref readonly Guid GetIID<T>() where T : unmanaged, IComIID
                    {
                        T local = default;
                        return ref ((IComIID)local).Guid;
                    }

                    public static Guid M() => GetIID<IRunningObjectTable>();
                }
                """;

        this.compilation = this.AddCode(consumerSnippet);
        this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false);
    }

    [Fact]
    public void FunctionPointersAsParameters()
    {
        this.GenerateApi("IContextCallback");
        MethodDeclarationSyntax method = this.FindGeneratedMethod("ContextCallback").Single(m => m.Parent is InterfaceDeclarationSyntax);
        ParameterSyntax parameter = method.ParameterList.Parameters[0];
        Assert.Contains(
            parameter.AttributeLists,
            al => al.Attributes.Any(a =>
            a is
            {
                Name: IdentifierNameSyntax { Identifier.ValueText: "MarshalAs" },
                ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "FunctionPtr" } } }],
            }));
    }

    [Fact]
    public void NoFunctionPointerForFARPROC()
    {
        this.GenerateApi("GetProcAddress");
        MethodDeclarationSyntax method = this.FindGeneratedMethod("GetProcAddress").Single(m => m.Modifiers.Any(SyntaxKind.ExternKeyword));
        Assert.DoesNotContain(
            method.AttributeLists,
            al => al.Target is { Identifier.RawKind: (int)SyntaxKind.ReturnKeyword } && al.Attributes.Any(a =>
            a is
            {
                Name: IdentifierNameSyntax { Identifier.ValueText: "MarshalAs" },
                ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "FunctionPtr" } } }],
            }));
    }

    [Theory, PairwiseData]
    public void IUnknown_QueryInterfaceGenericHelper(bool friendlyOverloads)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = false, FriendlyOverloads = new GeneratorOptions.FriendlyOverloadOptions { Enabled = friendlyOverloads } });

        this.GenerateApi("IUnknown");
        bool matchFound = this.FindGeneratedMethod("QueryInterface").Any(m => m.TypeParameterList?.Parameters.Count == 1);
        Assert.Equal(friendlyOverloads, matchFound);
    }

    [Fact]
    public void IUnknown_Derived_QueryInterfaceGenericHelper()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = false });

        this.GenerateApi("ITypeLib");
        Assert.Contains(
            this.FindGeneratedMethod("QueryInterface"),
            m => m.Parent is StructDeclarationSyntax { Identifier.Text: "ITypeLib" } && m.TypeParameterList?.Parameters.Count == 1);
    }

    [Theory, PairwiseData]
    public void TestGenerateCoCreateableClass(bool useIntPtrForComOutPtr)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = false, ComInterop = new GeneratorOptions.ComInteropOptions { UseIntPtrForComOutPointers = useIntPtrForComOutPtr } });

        this.GenerateApi("ShellLink");

        var shellLinkType = Assert.Single(this.FindGeneratedType("ShellLink"));

        // Check that it does not have the ComImport attribute.
        Assert.DoesNotContain(shellLinkType.AttributeLists, al => al.Attributes.Any(attr => attr.Name.ToString().Contains("ComImport")));

        if (!useIntPtrForComOutPtr)
        {
            // Check that it contains a CreateInstance method
            Assert.Contains(shellLinkType.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.Text == "CreateInstance");
        }
    }

    [Theory]
    [InlineData(true, "net472")]
    [InlineData(true, "net8.0")]
    [InlineData(false, "net472")]
    [InlineData(false, "net8.0")]
    public void COMInterfaceStructReturn(bool allowMarshaling, string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));

        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling });

        Assert.True(this.generator.TryGenerate("ID2D1RenderTarget", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("ID3D12Device1", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("ID3D12Device9", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("ID3D12StateObjectProperties2", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var methods = this.FindGeneratedMethod("GetSize");

        // TODO: Check "GetResourceAllocationInfo"
    }

    /// <summary>
    /// Regression test for <see href="https://github.com/microsoft/CsWin32/issues/1703">issue 1703</see>:
    /// generated COM struct wrappers contain CCW thunks annotated with
    /// <c>[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]</c>, which trips
    /// <c>CS3016 "Arrays as attribute arguments is not CLS-compliant"</c> under
    /// <c>[assembly: CLSCompliant(true)]</c>. The generator must mark such COM struct wrappers
    /// <c>[CLSCompliant(false)]</c> so consumers do not have to hand-author partials per type.
    /// </summary>
    [Theory]
    [CombinatorialData]
    public void COMStructWrappers_AreCLSCompliantFalse_Issue1703(
        [CombinatorialValues("net8.0", "net9.0", "net10.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(GetLanguageVersionForTfm(tfm) ?? LanguageVersion.Latest);
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });

        // The exact NativeMethods.txt content from the issue repro.
        Assert.True(this.generator.TryGenerate("ITypeInfo", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("ITypeLib", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("IRecordInfo", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Every generated COM struct wrapper that carries CCW thunks (annotated with
        // [UnmanagedCallersOnly(CallConvs = new[]{...})]) must itself bear [CLSCompliant(false)].
        foreach (string structName in new[] { "ITypeInfo", "ITypeLib", "IRecordInfo" })
        {
            var type = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType(structName).Single());
            Assert.Contains(
                type.AttributeLists.SelectMany(al => al.Attributes),
                a => a.Name.ToString() is "CLSCompliant" or "System.CLSCompliant"
                    && a.ArgumentList?.Arguments.Count == 1
                    && a.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax { Token.ValueText: "false" });
        }

        // End-to-end: a consuming assembly marked [assembly: CLSCompliant(true)] must compile
        // entirely clean — no CS3016, no CS3019, no CS3021. The generator suppresses CS3019/CS3021
        // in the generated-file pragma so consumers do not have to author per-file overrides.
        this.compilation = this.AddCode("""
            using System;

            [assembly: CLSCompliant(true)]

            internal static class Issue1703Consumer
            {
                public static void Touch()
                {
                    // Mere presence of the generated types in this CLS-compliant assembly used to trip CS3016.
                    _ = typeof(Windows.Win32.System.Com.ITypeInfo);
                    _ = typeof(Windows.Win32.System.Com.ITypeLib);
                    _ = typeof(Windows.Win32.System.Ole.IRecordInfo);
                }
            }
            """);
        this.AssertNoDiagnostics(this.compilation, logAllGeneratedCode: false);
    }

    /// <summary>
    /// Negative coverage for #1703: on downlevel TFMs (<c>net472</c>, <c>netstandard2.0</c>)
    /// the generator does not emit <c>[UnmanagedCallersOnly]</c>-decorated CCW thunks, so there is
    /// no array-valued attribute argument and no CS3016 to suppress. The generator must therefore
    /// not emit <c>[CLSCompliant(false)]</c> in that case — the attribute would be unmotivated noise.
    /// </summary>
    [Theory]
    [CombinatorialData]
    public void COMStructWrappers_NoCLSCompliantFalse_OnDownlevelTFMs_Issue1703(
        [CombinatorialValues("net472", "netstandard2.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false });

        Assert.True(this.generator.TryGenerate("ITypeInfo", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);

        var type = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("ITypeInfo").Single());
        Assert.DoesNotContain(
            type.AttributeLists.SelectMany(al => al.Attributes),
            a => a.Name.ToString() is "CLSCompliant" or "System.CLSCompliant");
    }

    /// <summary>
    /// Negative coverage for #1703: when the generator is configured to emit public types
    /// (<see cref="GeneratorOptions.Public"/> = <see langword="true"/>), the COM struct wrapper
    /// is part of the consumer's CLS surface and they own its CLS-compliance contract — the
    /// generator must not unilaterally stamp <c>[CLSCompliant(false)]</c> on it.
    /// </summary>
    [Fact]
    public void COMStructWrappers_NoCLSCompliantFalse_WhenPublic_Issue1703()
    {
        this.compilation = this.starterCompilations["net10.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(GetLanguageVersionForTfm("net10.0") ?? LanguageVersion.Latest);
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = false, Public = true });

        Assert.True(this.generator.TryGenerate("ITypeInfo", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);

        var type = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("ITypeInfo").Single());
        Assert.DoesNotContain(
            type.AttributeLists.SelectMany(al => al.Attributes),
            a => a.Name.ToString() is "CLSCompliant" or "System.CLSCompliant");
    }
}
