// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class GeneratorTests : GeneratorTestBase
{
    public GeneratorTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, PairwiseData]
    public void AssemblyAttributeGenerated(bool emitSingleFile)
    {
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { EmitSingleFile = emitSingleFile });
        this.GenerateApi("GetTickCount");
        IEnumerable<AttributeSyntax> assemblyMetadataAttributes =
            from tree in this.compilation.SyntaxTrees
            from attributeList in tree.GetCompilationUnitRoot().AttributeLists
            where attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) is true
            from attribute in attributeList.Attributes
            where attribute.Name.ToString() == "global::System.Reflection.AssemblyMetadata"
            select attribute;
        AttributeSyntax cswin32Stamp = Assert.Single(assemblyMetadataAttributes);
        Assert.Equal("Microsoft.Windows.CsWin32", ((LiteralExpressionSyntax?)cswin32Stamp.ArgumentList?.Arguments[0].Expression)?.Token.Value);
        Assert.Equal(ThisAssembly.AssemblyInformationalVersion, ((LiteralExpressionSyntax?)cswin32Stamp.ArgumentList?.Arguments[1].Expression)?.Token.Value);
    }

    [Fact]
    public void NoGeneration()
    {
        this.generator = this.CreateGenerator();
        Assert.Empty(this.generator.GetCompilationUnits(CancellationToken.None));
    }

    [Theory]
    [InlineData("COPYFILE2_CALLBACK_NONE", "COPYFILE2_MESSAGE_TYPE")]
    [InlineData("RTL_RUN_ONCE_ASYNC", null)]
    [InlineData("__zz__not_defined", null)]
    public void TryGetEnumName(string candidate, string? declaringEnum)
    {
        this.generator = this.CreateGenerator();

        Assert.Equal(declaringEnum is object, this.generator.TryGetEnumName(candidate, out string? actualDeclaringEnum));
        Assert.Equal(declaringEnum, actualDeclaringEnum);
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void CoCreateInstance(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(includeDocs: true);
        Assert.True(this.generator.TryGenerateExternMethod("CoCreateInstance", out _));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory]
    [MemberData(nameof(TFMData))]
    public void SimplestMethod(string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator(includeDocs: true);
        const string methodName = "GetTickCount";
        Assert.True(this.generator.TryGenerateExternMethod(methodName, out _));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var generatedMethod = this.FindGeneratedMethod(methodName).Single();
        if (tfm is "net8.0" or "net9.0")
        {
            Assert.Contains(generatedMethod.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }
        else
        {
            Assert.DoesNotContain(generatedMethod.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform"));
        }

        if (tfm != "net35")
        {
            Assert.Contains(generatedMethod.AttributeLists, al => IsAttributePresent(al, "DefaultDllImportSearchPaths"));
        }
    }

    [Fact]
    public void DbgHelpExternMethodsCanLoadAppLocal()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerateExternMethod("DbgHelpCreateUserDump", out _));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod("DbgHelpCreateUserDump"), m => m.Modifiers.Any(SyntaxKind.ExternKeyword));
        AttributeSyntax searchPathsAttribute = Assert.Single(method.AttributeLists.SelectMany(al => al.Attributes), a => a.Name is IdentifierNameSyntax { Identifier: { ValueText: "DefaultDllImportSearchPaths" } });
        Assert.NotNull(searchPathsAttribute.ArgumentList);
        Assert.Single(searchPathsAttribute.ArgumentList.DescendantNodes().OfType<IdentifierNameSyntax>(), id => id.Identifier.ValueText == nameof(DllImportSearchPath.ApplicationDirectory));
    }

    [Theory]
    [PairwiseData]
    public void TemplateProvidedMembersMatchVisibilityWithContainingType_Methods(bool generatePublic)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { Public = generatePublic });
        Assert.True(this.generator.TryGenerate("HRESULT", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax? generatedMethod = this.FindGeneratedMethod("ThrowOnFailure").Single();
        SyntaxKind expectedVisibility = generatePublic ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword;
        Assert.True(generatedMethod.Modifiers.Any(expectedVisibility));
    }

    [Theory]
    [PairwiseData]
    public void TemplateProvidedMembersMatchVisibilityWithContainingType_OtherMemberTypes(bool generatePublic)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { Public = generatePublic });
        Assert.True(this.generator.TryGenerate("PCSTR", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        StructDeclarationSyntax pcstrType = (StructDeclarationSyntax)this.FindGeneratedType("PCSTR").Single();
        SyntaxKind expectedVisibility = generatePublic ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword;

        // Assert fields
        Assert.Contains(pcstrType.Members.OfType<FieldDeclarationSyntax>(), f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "Value") && f.Modifiers.Any(expectedVisibility));

        // Assert properties
        Assert.Contains(pcstrType.Members.OfType<PropertyDeclarationSyntax>(), p => p.Identifier.ValueText == "Length" && p.Modifiers.Any(expectedVisibility));

        // Assert constructors
        Assert.All(pcstrType.Members.OfType<ConstructorDeclarationSyntax>(), c => c.Modifiers.Any(expectedVisibility));

        // Assert that private members remain private.
        Assert.Contains(pcstrType.Members.OfType<PropertyDeclarationSyntax>(), p => p.Identifier.ValueText == "DebuggerDisplay" && p.Modifiers.Any(SyntaxKind.PrivateKeyword));
    }

    [Fact]
    public void SupportedOSPlatform_AppearsOnFriendlyOverloads()
    {
        const string methodName = "GetStagedPackagePathByFullName2";
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(methodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.All(this.FindGeneratedMethod(methodName), method => Assert.Contains(method.AttributeLists, al => IsAttributePresent(al, "SupportedOSPlatform")));
    }

    [Theory]
    [CombinatorialData]
    public void InterestingAPIs(
        [CombinatorialValues(
            "PSTR",
            "PWSTR",
            "PCSTR",
            "PCWSTR",
            "PZZSTR",
            "PZZWSTR",
            "PCZZSTR",
            "PCZZWSTR",
            "RoCreatePropertySetSerializer", // References a WinRT API
            "LocalLock", // returns HLOCAL, which requires special release support
            "LoadLibraryEx", // method with a reserved parameter
            "IEnumNetCfgComponent", // interface with a method containing an `[Reserved] out` parameter (bonkers, I know).
            "IEventSubscription",
            "IRealTimeStylusSynchronization", // uses the `lock` C# keyword.
            "IHTMLInputElement", // has a field named `checked`, a C# keyword.
            "NCryptImportKey", // friendly overload takes SafeHandle backed by a UIntPtr instead of IntPtr
            "IUIAutomation", // non-preservesig retval COM method with a array size index parameter
            "IHTMLWindow2", // contains properties named with C# reserved keywords
            "CreateFile", // built-in SafeHandle use
            "CreateCursor", // 0 or -1 invalid SafeHandle generated
            "PlaySound", // 0 invalid SafeHandle generated
            "SLIST_HEADER", // Union struct that defined uniquely for each CPU architecture
            "PROFILER_HEAP_OBJECT_OPTIONAL_INFO",
            "MI_Instance", // recursive type where managed testing gets particularly tricky
            "CertFreeCertificateChainList", // double pointer extern method
            "D3DGetTraceInstructionOffsets", // SizeParamIndex
            "PlgBlt", // SizeConst
            "IWebBrowser", // Navigate method has an [In, Optional] object parameter
            "ENABLE_TRACE_PARAMETERS_V1", // bad xml created at some point.
            "JsRuntimeVersion", // An enum that has an extra member in a separate header file.
            "ReportEvent", // Failed at one point
            "DISPLAYCONFIG_VIDEO_SIGNAL_INFO", // Union, explicit layout, bitmask, nested structs
            "MFVideoAlphaBitmap", // field named params
            "DDRAWI_DDVIDEOPORT_INT", // field that is never used
            "MainAVIHeader", // dwReserved field is a fixed length array
            "RpcServerRegisterIfEx", // Optional attribute on delegate type.
            "RpcSsSwapClientAllocFree", // Parameters typed as pointers to in delegates and out delegates
            "RPC_DISPATCH_TABLE", // Struct with a field typed as a delegate
            "RPC_SERVER_INTERFACE", // Struct with a field typed as struct with a field typed as a delegate
            "DDHAL_DESTROYDRIVERDATA", // Struct with a field typed as a delegate
            "I_RpcServerInqAddressChangeFn", // p/invoke that returns a function pointer
            "WSPUPCALLTABLE", // a delegate with a delegate in its signature
            "BOOL", // a special cased typedef struct
            "uregex_getMatchCallback", // friendly overload with delegate parameter, and out parameters
            "CreateDispatcherQueueController", // References a WinRT type
            "RegOpenKey", // allocates a handle with a release function that returns LSTATUS
            "LsaRegisterLogonProcess", // allocates a handle with a release function that returns NTSTATUS
            "FilterCreate", // allocates a handle with a release function that returns HRESULT
            "DsGetDcOpen", // allocates a handle with a release function that returns HRESULT
            "DXVAHDSW_CALLBACKS", // pointers to handles
            "HBITMAP_UserMarshal", // in+out handle pointer
            "GetDiskFreeSpaceExW", // ULARGE_INTEGER replaced with keyword: ulong.
            "MsiGetProductPropertyW", // MSIHANDLE (a 32-bit handle)
            "TCP_OPT_SACK", // nested structs with inline arrays with nested struct elements
            "HANDLETABLE", // nested structs with inline arrays with nint element
            "SYSTEM_POLICY_INFORMATION", // nested structs with inline arrays with IntPtr element
            "D3D11_BLEND_DESC1", // nested structs with inline arrays with element that is NOT nested
            "RTM_DEST_INFO", // nested structs with inline arrays with element whose name collides with another
            "DISPPARAMS",
            "PICTYPE", // An enum with -1 as an enum value
            "CoCreateInstance", // a hand-written friendly overload
            "JsVariantToValue",
            "IOleUILinkContainerW", // An IUnknown-derived interface with no GUID
            "FILE_TYPE_NOTIFICATION_INPUT",
            "DS_SELECTION_LIST", // A struct with a fixed-length inline array of potentially managed structs
            "ISpellCheckerFactory", // COM interface that includes `ref` parameters
            "LocalSystemTimeToLocalFileTime", // small step
            "WSAHtons", // A method that references SOCKET (which is typed as UIntPtr) so that a SafeHandle will be generated.
            "IDelayedPropertyStoreFactory", // interface inheritance across namespaces
            "D3D9ON12_ARGS", // Contains an inline array of IUnknown objects
            "NCryptOpenKey", // Generates a SafeHandle based on a UIntPtr
            "CIDLData_CreateFromIDArray", // Method with out parameter of a possibly marshaled interop type shared with the BCL,
            "ID3D12Resource", // COM interface with base types
            "OpenTrace", // the CloseTrace method called by the SafeHandle returns WIN32_ERROR. The handle is ALWAYS 64-bits.
            "QueryTraceProcessingHandle", // uses a handle that is always 64-bits, even in 32-bit processes
            "ID2D1RectangleGeometry", // COM interface with base types
            "IGraphicsEffectD2D1Interop")] // COM interface that refers to C#/WinRT types
        string api,
        [CombinatorialValues("netstandard2.0", "net9.0")]
        string tfm,
        bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with
        {
            WideCharOnly = false,
            AllowMarshaling = allowMarshaling,
        };
        this.compilation = this.starterCompilations[tfm].WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Verifies that GetLastError is never generated.
    /// Users should call <see cref="Marshal.GetLastWin32Error"/> instead.
    /// </summary>
    [Fact]
    public void GetLastErrorNotIncludedInBulkGeneration()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("kernel32.*", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        Assert.True(this.IsMethodGenerated("CreateFile"));
        Assert.False(this.IsMethodGenerated("GetLastError"));
    }

    [Theory, PairwiseData]
    public void WildcardForConstants(bool withNamespace)
    {
        this.generator = this.CreateGenerator();
        string ns = withNamespace ? "Windows.Win32.Security.Cryptography." : string.Empty;
        Assert.True(this.generator.TryGenerate(ns + "ALG_SID_MD*", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedConstant("ALG_SID_MD2"));
        Assert.Single(this.FindGeneratedConstant("ALG_SID_MD4"));
        Assert.Empty(this.FindGeneratedConstant("ALG_SID_HMAC"));
    }

    /// <summary>
    /// Asserts that the source generator will not emit a warning when a wildcard is used to generate constants that match in more than one metadata assembly.
    /// </summary>
    [Fact]
    public void WildcardForConstants_DefinedAcrossMetadata()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("IOCTL_*", out IReadOnlyCollection<string> preciseApi, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedConstant("IOCTL_ABORT_PIPE")); // Win32
        Assert.Single(this.FindGeneratedConstant("IOCTL_REDIR_QUERY_PATH")); // WDK

        // If this produces more than one result, the source generator will complain.
        Assert.Single(preciseApi);
    }

    [Fact]
    public void WildcardForConstants_NoMatch()
    {
        this.generator = this.CreateGenerator();
        Assert.False(this.generator.TryGenerate("IDONTEXIST*", out IReadOnlyCollection<string> preciseApi, CancellationToken.None));
        Assert.Empty(preciseApi);
    }

    [Theory, PairwiseData]
    public void UnionWithRefAndValueTypeFields(
        [CombinatorialValues("VARDESC", "VARIANT")] string typeName,
        [CombinatorialValues("net8.0", "net472", "netstandard2.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(typeName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Generate APIs that depend on common APIs but in both marshalable and non-marshalable contexts.
    /// </summary>
    [Fact]
    public void UnionWithRefAndValueTypeFields2()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("MI_MethodDecl", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("MI_Value", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void TypeDefConstantsDeclaredWithinTypeDef()
    {
        const string constant = "S_OK";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(constant, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        FieldDeclarationSyntax field = this.FindGeneratedConstant(constant).Single();
        StructDeclarationSyntax declaringStruct = Assert.IsType<StructDeclarationSyntax>(field.Parent);
        Assert.Equal("HRESULT", declaringStruct.Identifier.ToString());
    }

    [Fact]
    public void TypeDefConstantsRedirectedToPInvokeWhenTypeDefAlreadyInRefAssembly()
    {
        CSharpCompilation referencedProject = this.compilation.WithAssemblyName("refdProj");

        using var referencedGenerator = this.CreateGenerator(new GeneratorOptions { Public = true }, referencedProject);
        Assert.True(referencedGenerator.TryGenerate("HRESULT", CancellationToken.None));
        referencedProject = this.AddGeneratedCode(referencedProject, referencedGenerator);
        this.AssertNoDiagnostics(referencedProject);

        // Now produce more code in a referencing project that includes at least one of the same types as generated in the referenced project.
        const string constant = "S_OK";
        this.compilation = this.compilation.AddReferences(referencedProject.ToMetadataReference());
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(constant, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        FieldDeclarationSyntax field = this.FindGeneratedConstant(constant).Single();
        ClassDeclarationSyntax declaringClass = Assert.IsType<ClassDeclarationSyntax>(field.Parent);
        Assert.Equal("PInvoke", declaringClass.Identifier.ToString());
    }

    [Theory]
    [InlineData("IDC_ARROW")] // PCWSTR / PWSTR
    public void SpecialTypeDefsDoNotContainTheirOwnConstants(string constantName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(constantName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        FieldDeclarationSyntax field = this.FindGeneratedConstant(constantName).Single();
        ClassDeclarationSyntax declaringClass = Assert.IsType<ClassDeclarationSyntax>(field.Parent);
        Assert.Equal("PInvoke", declaringClass.Identifier.ToString());
    }

    [Theory, CombinatorialData]
    public void Decimal([CombinatorialValues("net472", "net8.0")] string tfm)
    {
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithPreprocessorSymbols(this.preprocessorSymbolsByTfm[tfm]);
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("DECIMAL", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void AmbiguousApiName()
    {
        this.generator = this.CreateGenerator();
        Assert.False(this.generator.TryGenerate("IDENTITY_TYPE", out IReadOnlyCollection<string> preciseApi, CancellationToken.None));
        Assert.Equal(2, preciseApi.Count);
        Assert.Contains("Windows.Win32.NetworkManagement.NetworkPolicyServer.IDENTITY_TYPE", preciseApi);
        Assert.Contains("Windows.Win32.Security.Authentication.Identity.Provider.IDENTITY_TYPE", preciseApi);
    }

    [Fact]
    public void ObsoleteAttributePropagated()
    {
        const string StructName = "IMAGE_OPTIONAL_HEADER32";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(StructName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        StructDeclarationSyntax structDecl = (StructDeclarationSyntax)this.FindGeneratedType(StructName).Single();
        (FieldDeclarationSyntax Field, VariableDeclaratorSyntax Variable)? field = this.FindFieldDeclaration(structDecl, "LoaderFlags");
        Assert.NotNull(field);
        Assert.Contains(field!.Value.Field.AttributeLists, al => IsAttributePresent(al, "Obsolete"));
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("BOOLEAN")]
    [InlineData("HRESULT")]
    [InlineData("NTSTATUS")]
    [InlineData("PCSTR")]
    [InlineData("PCWSTR")]
    [InlineData("RECT")]
    [InlineData("SIZE")]
    [InlineData("SYSTEMTIME")]
    public void TemplateAPIsGenerate(string handleType)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(handleType, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void HasGeneratedCodeAttribute()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("IDebugDocumentInfo", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("HANDLE", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("INPUT_RECORD", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("GetTickCount", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("ACTIVATE_KEYBOARD_LAYOUT_FLAGS", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("PAINTSTRUCT", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        InterfaceDeclarationSyntax interfaceDecl = Assert.IsType<InterfaceDeclarationSyntax>(this.FindGeneratedType("IDebugDocumentInfo").Single());
        Assert.Contains(interfaceDecl.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));

        StructDeclarationSyntax handle = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("HANDLE").Single());
        Assert.Contains(handle.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));

        StructDeclarationSyntax structDecl = Assert.IsType<StructDeclarationSyntax>(this.FindGeneratedType("INPUT_RECORD").Single());
        Assert.Contains(structDecl.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));

        IEnumerable<ClassDeclarationSyntax> pInvokeClass = this.FindGeneratedType("PInvoke").OfType<ClassDeclarationSyntax>();
        Assert.Contains(pInvokeClass, c => c.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode"))));

        EnumDeclarationSyntax enumDecl = Assert.IsType<EnumDeclarationSyntax>(this.FindGeneratedType("ACTIVATE_KEYBOARD_LAYOUT_FLAGS").Single());
        Assert.Contains(enumDecl.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));

        ClassDeclarationSyntax arrayExtensions = Assert.IsType<ClassDeclarationSyntax>(this.FindGeneratedType("InlineArrayIndexerExtensions").Single());
        Assert.Contains(arrayExtensions.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));

        Assert.All(
            this.compilation.SyntaxTrees.SelectMany(st => st.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()).Where(btd => btd.Identifier.ValueText.EndsWith("_Extensions", StringComparison.Ordinal)),
            e => Assert.Contains(e.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode"))));

        ClassDeclarationSyntax sysFreeStringSafeHandleClass = Assert.IsType<ClassDeclarationSyntax>(this.FindGeneratedType("SysFreeStringSafeHandle").Single());
        Assert.Contains(sysFreeStringSafeHandleClass.AttributeLists, al => al.Attributes.Any(a => a.Name.ToString().Contains("GeneratedCode")));
    }

    /// <summary>
    /// GetMessage should return BOOL rather than bool because it actually returns any of THREE values.
    /// </summary>
    [Fact]
    public void GetMessageW_ReturnsBOOL()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("GetMessage", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.All(this.FindGeneratedMethod("GetMessage"), method => Assert.True(method.ReturnType is QualifiedNameSyntax { Right: { Identifier: { ValueText: "BOOL" } } }));
    }

    [Theory, PairwiseData]
    public void NativeArray_OfManagedTypes_MarshaledAsLPArray(bool allowMarshaling)
    {
        const string ifaceName = "ID3D11DeviceContext";
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate(ifaceName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        var generatedMethod = this.FindGeneratedMethod("OMSetRenderTargets").FirstOrDefault(m => m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[0].Identifier.ValueText == "NumViews" && (!allowMarshaling || m.Parent is InterfaceDeclarationSyntax));
        Assert.NotNull(generatedMethod);

        if (allowMarshaling)
        {
            Assert.Contains(generatedMethod!.ParameterList.Parameters[1].AttributeLists, al => IsAttributePresent(al, "MarshalAs"));
        }
        else
        {
            Assert.DoesNotContain(generatedMethod!.ParameterList.Parameters[1].AttributeLists, al => IsAttributePresent(al, "MarshalAs"));
        }
    }

    [Theory, PairwiseData]
    public void NativeArray_SizeParamIndex_ProducesSimplerFriendlyOverload(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("EvtNext", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        IEnumerable<MethodDeclarationSyntax> overloads = this.FindGeneratedMethod("EvtNext");
        Assert.Contains(overloads, o => o.ParameterList.Parameters.Count == 5 && (o.ParameterList.Parameters[1].Type?.ToString().StartsWith("Span<", StringComparison.Ordinal) ?? false));
    }

    [Theory, PairwiseData]
    public void BOOL_ReturnType_InCOMInterface(bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("ISpellCheckerFactory", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        if (allowMarshaling)
        {
            Assert.Contains(this.FindGeneratedMethod("IsSupported"), method => method.ReturnType is QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "BOOL" } } });
        }
        else
        {
            Assert.Contains(this.FindGeneratedMethod("IsSupported"), method => method.ParameterList.Parameters.Last().Type is PointerTypeSyntax { ElementType: QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { ValueText: "BOOL" } } } });
        }
    }

    [Theory, PairwiseData]
    public void GenerateByNamespace(bool correctCase)
    {
        this.generator = this.CreateGenerator();
        string ns = "Windows.Win32.Foundation";
        if (!correctCase)
        {
            ns = ns.ToUpperInvariant();
        }

        Assert.True(this.generator.TryGenerate(ns, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.NotEmpty(this.FindGeneratedType("BOOL"));
        Assert.NotEmpty(this.FindGeneratedType("SIZE"));
    }

    /// <summary>
    /// Verifies that fields are not converted from BOOL to bool.
    /// </summary>
    [Fact]
    public void BOOL_FieldRemainsBOOL()
    {
        this.GenerateApi("ICONINFO");
        var theStruct = (StructDeclarationSyntax)this.FindGeneratedType("ICONINFO").Single();
        VariableDeclarationSyntax field = theStruct.Members.OfType<FieldDeclarationSyntax>().Select(m => m.Declaration).Single(d => d.Variables.Any(v => v.Identifier.ValueText == "fIcon"));
        Assert.Equal("BOOL", Assert.IsType<QualifiedNameSyntax>(field.Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void TypeNameCollisionsDoNotCauseTooMuchCodeGen()
    {
        this.GenerateApi("TYPEDESC");
        Assert.Empty(this.FindGeneratedType("D3DMATRIX"));
    }

    [Fact]
    public void Const_PWSTR_Becomes_PCWSTR_and_String()
    {
        this.GenerateApi("StrCmpLogical");

        bool foundPCWSTROverload = false;
        bool foundStringOverload = false;
        IEnumerable<MethodDeclarationSyntax> overloads = this.FindGeneratedMethod("StrCmpLogical");
        foreach (MethodDeclarationSyntax method in overloads)
        {
            foundPCWSTROverload |= method!.ParameterList.Parameters[0].Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } };
            foundStringOverload |= method!.ParameterList.Parameters[0].Type?.ToString() == "string";
        }

        Assert.True(foundPCWSTROverload, "PCWSTR overload is missing.");
        Assert.True(foundStringOverload, "string overload is missing.");
        Assert.Equal(2, overloads.Count());
    }

    ////[Fact]
    ////public void CrossWinmdTypeReference()
    ////{
    ////    this.generator = this.CreateGenerator();
    ////    using Generator diaGenerator = this.CreateGenerator(DiaMetadataPath);
    ////    var super = SuperGenerator.Combine(this.generator, diaGenerator);
    ////    Assert.True(diaGenerator.TryGenerate("E_PDB_NOT_FOUND", CancellationToken.None));
    ////    this.CollectGeneratedCode(this.generator);
    ////    this.CollectGeneratedCode(diaGenerator);
    ////    this.AssertNoDiagnostics();

    ////    Assert.Single(this.FindGeneratedType("HRESULT"));
    ////    Assert.Single(this.FindGeneratedConstant("E_PDB_NOT_FOUND"));
    ////}

    [Theory, CombinatorialData]
    public void ArchitectureSpecificAPIsTreatment(
        [CombinatorialValues("MEMORY_BASIC_INFORMATION", "SP_PROPCHANGE_PARAMS", "JsCreateContext", "IShellBrowser")] string apiName,
        [CombinatorialValues(Platform.AnyCpu, Platform.X64, Platform.X86)] Platform platform,
        bool allowMarshaling)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        if (platform == Platform.AnyCpu)
        {
            // AnyCPU targets should throw an exception with a helpful error message when asked for arch-specific APIs
            var ex = Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate(apiName, CancellationToken.None));
            this.logger.WriteLine(ex.Message);
            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics();
        }
        else
        {
            // Arch-specific compilations should generate the requested APIs.
            Assert.True(this.generator.TryGenerate(apiName, CancellationToken.None));
            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics();
        }
    }

    [Fact]
    public void MultipleEntrypointsToOmittedArchSpecificApis()
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.AnyCpu));
        this.generator = this.CreateGenerator();

        // Request a struct that depends on arch-specific IP6_ADDRESS.
        Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate("DNS_SERVICE_INSTANCE", CancellationToken.None));

        // Request a struct that depends on DNS_SERVICE_INSTANCE.
        Assert.ThrowsAny<GenerationFailedException>(() => this.generator.TryGenerate("DNS_SERVICE_REGISTER_REQUEST", CancellationToken.None));

        // Verify that no uncompilable code was generated.
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, CombinatorialData]
    public void TypeRefsToArchSpecificApis(
        [CombinatorialValues(Platform.X64, Platform.X86)] Platform platform)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator();

        // Request a struct directly, and indirectly through another that references it.
        // This verifies that even if the metadata references a particular arch of the structure,
        // the right one for the CPU architecture is generated.
        Assert.True(this.generator.TryGenerate("SP_PROPCHANGE_PARAMS", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("SP_CLASSINSTALL_HEADER", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        // Verify that [Pack = 1] appears on both structures.
        foreach (string structName in new[] { "SP_CLASSINSTALL_HEADER", "SP_PROPCHANGE_PARAMS" })
        {
            var header = this.FindGeneratedType(structName).Single();
            SeparatedSyntaxList<AttributeArgumentSyntax> attributes = header.AttributeLists.SelectMany(al => al.Attributes).FirstOrDefault(att => att.Name is IdentifierNameSyntax { Identifier: { ValueText: "StructLayout" } })?.ArgumentList?.Arguments ?? default;
            Predicate<AttributeArgumentSyntax> matchPredicate = att => att.NameEquals is { Name: { Identifier: { ValueText: "Pack" } } };
            if (platform == Platform.X86)
            {
                Assert.Contains(attributes, matchPredicate);
            }
            else
            {
                Assert.DoesNotContain(attributes, matchPredicate);
            }
        }
    }

    [Theory, PairwiseData]
    public void FARPROC_AsFieldType(bool allowMarshaling)
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { AllowMarshaling = allowMarshaling });
        Assert.True(this.generator.TryGenerate("EXTPUSH", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        StructDeclarationSyntax type = Assert.IsType<StructDeclarationSyntax>(Assert.Single(this.FindGeneratedType("_Anonymous1_e__Union")));
        var callback = (FieldDeclarationSyntax)type.Members[1];
        Assert.True(callback.Declaration.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "FARPROC" } } }, "Field type was " + callback.Declaration.Type);
    }

    [Fact]
    public void GetLastErrorGenerationThrowsWhenExplicitlyCalled()
    {
        this.generator = this.CreateGenerator();
        Assert.Throws<NotSupportedException>(() => this.generator.TryGenerate("GetLastError", CancellationToken.None));
    }

    [Fact]
    public void DeleteObject_TakesTypeDefStruct()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("DeleteObject", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        MethodDeclarationSyntax? deleteObjectMethod = this.FindGeneratedMethod("DeleteObject").FirstOrDefault();
        Assert.NotNull(deleteObjectMethod);
        Assert.Equal("HGDIOBJ", Assert.IsType<QualifiedNameSyntax>(deleteObjectMethod!.ParameterList.Parameters[0].Type).Right.Identifier.ValueText);
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("BSTR")]
    [InlineData("HRESULT")]
    [InlineData("NTSTATUS")]
    [InlineData("PCSTR")]
    [InlineData("PCWSTR")]
    [InlineData("PWSTR")]
    public void SynthesizedTypesCanBeDirectlyRequested(string synthesizedTypeName)
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(synthesizedTypeName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType(synthesizedTypeName));
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("BSTR")]
    [InlineData("HRESULT")]
    [InlineData("NTSTATUS")]
    [InlineData("PCSTR")]
    [InlineData("PCWSTR")]
    [InlineData("PWSTR")]
    public void SynthesizedTypesWorkInNet35(string synthesizedTypeName)
    {
        this.compilation = this.starterCompilations["net35"];
        this.generator = this.CreateGenerator();

        Assert.True(this.generator.TryGenerate(synthesizedTypeName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.Single(this.FindGeneratedType(synthesizedTypeName));
    }

    [Theory, PairwiseData]
    public void NoFriendlyOverloadsWithSpanInNet35(bool allowMarshaling)
    {
        this.compilation = this.starterCompilations["net35"];
        var options = DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling };
        this.generator = this.CreateGenerator(options);
        Assert.True(this.generator.TryGenerate("EvtNext", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Fact]
    public void InOutPWSTRGetsRefSpanCharFriendlyOverload()
    {
        const string MethodName = "PathParseIconLocation";
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate(MethodName, CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        IEnumerable<MethodDeclarationSyntax> generatedMethods = this.FindGeneratedMethod(MethodName);
        Assert.Contains(generatedMethods, m => m.ParameterList.Parameters.Count == 1 && m.ParameterList.Parameters[0].Modifiers.Any(SyntaxKind.RefKeyword) && m.ParameterList.Parameters[0].Type?.ToString() == "Span<char>");
    }

    [Fact]
    public void UnicodeExtenMethodsGetCharSet()
    {
        const string MethodName = "VkKeyScan";
        this.GenerateApi(MethodName);
        MethodDeclarationSyntax generatedMethod = this.FindGeneratedMethod(MethodName).Single();
        Assert.Contains(
            generatedMethod.AttributeLists.SelectMany(al => al.Attributes),
            a => a.Name.ToString() == "DllImport" &&
            a.ArgumentList?.Arguments.Any(arg => arg is
            {
                NameEquals.Name.Identifier.ValueText: nameof(DllImportAttribute.CharSet),
                Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: nameof(CharSet.Unicode) } }
            }) is true);
    }

    [Fact]
    public void NullMethodsClass()
    {
        Assert.Throws<InvalidOperationException>(() => this.CreateGenerator(new GeneratorOptions { ClassName = null! }));
    }

    [Fact]
    public void RenamedMethodsClass()
    {
        this.generator = this.CreateGenerator(new GeneratorOptions { ClassName = "MyPInvoke" });
        Assert.True(this.generator.TryGenerate("GetTickCount", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("CDB_REPORT_BITS", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
        Assert.NotEmpty(this.FindGeneratedType("MyPInvoke"));
        Assert.Empty(this.FindGeneratedType("PInvoke"));
    }

    [Theory, PairwiseData]
    public void TBButton([CombinatorialMemberData(nameof(SpecificCpuArchitectures))] Platform platform)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(platform));
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("TBBUTTON", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    [Theory, PairwiseData]
    public void ProjectReferenceBetweenTwoGeneratingProjects(bool internalsVisibleTo)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));

        CSharpCompilation referencedProject = this.compilation
            .WithAssemblyName("refdProj");
        if (internalsVisibleTo)
        {
            referencedProject = referencedProject.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText($@"[assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute(""{this.compilation.AssemblyName}"")]", this.parseOptions, cancellationToken: TestContext.Current.CancellationToken));
        }

        using var referencedGenerator = this.CreateGenerator(new GeneratorOptions { ClassName = "P1" }, referencedProject);
        Assert.True(referencedGenerator.TryGenerate("LockWorkStation", CancellationToken.None));
        Assert.True(referencedGenerator.TryGenerate("CreateFile", CancellationToken.None));
        Assert.True(referencedGenerator.TryGenerate("RAWHID", CancellationToken.None));
        Assert.True(referencedGenerator.TryGenerate("SHFILEINFOW", CancellationToken.None)); // generates inline arrays + extension methods
        referencedProject = this.AddGeneratedCode(referencedProject, referencedGenerator);
        this.AssertNoDiagnostics(referencedProject);

        // Now produce more code in a referencing project that includes at least one of the same types as generated in the referenced project.
        this.compilation = this.compilation.AddReferences(referencedProject.ToMetadataReference());
        this.generator = this.CreateGenerator(new GeneratorOptions { ClassName = "P2" });
        Assert.True(this.generator.TryGenerate("HidD_GetAttributes", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("RAWHID", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("DROPDESCRIPTION", CancellationToken.None)); // reuses the same inline array and extension methods as SHFILEINFOW.
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();
    }

    /// <summary>
    /// Tests that a generating project that references two other generating projects will generate its own types if a unique type isn't available from the referenced projects.
    /// In particular, if a type is generated in <em>both</em> of the referenced projects, that creates an ambiguity problem that <em>may</em> be resolved with <c>extern alias</c>
    /// or perhaps by simply generating types a third time in the local compilation.
    /// </summary>
    /// <param name="internalsVisibleTo">Whether to generate internal APIs and use the <see cref="InternalsVisibleToAttribute"/>.</param>
    /// <param name="externAlias">Whether to specify extern aliases for the references.</param>
    [Theory, PairwiseData]
    public void ProjectReferenceBetweenThreeGeneratingProjects(bool internalsVisibleTo, bool externAlias)
    {
        CSharpCompilation templateCompilation = this.compilation;
        for (int i = 1; i <= 2; i++)
        {
            CSharpCompilation referencedProject = templateCompilation.WithAssemblyName("refdProj" + i);
            LogProject(referencedProject.AssemblyName!);
            if (internalsVisibleTo)
            {
                var ivtSource = CSharpSyntaxTree.ParseText($@"[assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute(""{this.compilation.AssemblyName}"")]", this.parseOptions, cancellationToken: TestContext.Current.CancellationToken);
                referencedProject = referencedProject.AddSyntaxTrees(ivtSource);
            }

            using var referencedGenerator = this.CreateGenerator(new GeneratorOptions { Public = !internalsVisibleTo }, referencedProject);

            // Both will declare HRESULT
            Assert.True(referencedGenerator.TryGenerate("HANDLE", CancellationToken.None));

            // One will declare FILE_SHARE_MODE
            if (i % 2 == 0)
            {
                Assert.True(referencedGenerator.TryGenerate("FILE_SHARE_MODE", CancellationToken.None));
            }

            referencedProject = this.AddGeneratedCode(referencedProject, referencedGenerator);
            this.AssertNoDiagnostics(referencedProject);
            Assert.Single(this.FindGeneratedType("HANDLE", referencedProject));

            ImmutableArray<string> aliases = externAlias ? ImmutableArray.Create("ref" + i) : ImmutableArray<string>.Empty;
            this.compilation = this.compilation.AddReferences(referencedProject.ToMetadataReference(aliases));
        }

        LogProject(this.compilation.AssemblyName!);

        // Now produce more code in a referencing project that needs HANDLE, which is found *twice*, once in each referenced project.
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreateFile", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("FILE_ACCESS_RIGHTS", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);

        // Consume the API to verify the user experience isn't broken.
        string programCsSource = @"
#pragma warning disable CS0436

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

class Program
{
    static unsafe void Main()
    {
        HANDLE h = PInvoke.CreateFile(
            default(PCWSTR),
            (uint)FILE_ACCESS_RIGHTS.FILE_ADD_FILE,
            FILE_SHARE_MODE.FILE_SHARE_READ,
            null,
            FILE_CREATION_DISPOSITION.CREATE_NEW,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_ARCHIVE,
            default(HANDLE));
    }
}
";
        this.compilation = this.compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(programCsSource, this.parseOptions, "Program.cs", cancellationToken: TestContext.Current.CancellationToken));

        this.AssertNoDiagnostics();

        // The CreateFile should of course be declared locally.
        Assert.NotEmpty(this.FindGeneratedMethod("CreateFile"));

        // We expect HANDLE to be declared locally, to resolve the ambiguity of it coming from *two* references.
        Assert.Single(this.FindGeneratedType("HANDLE"));

        // We expect FILE_SHARE_MODE to be declared locally only if not using extern aliases, since it can be retrieved from *one* of the references.
        Assert.Equal(externAlias, this.FindGeneratedType("FILE_SHARE_MODE").Any());

        void LogProject(string name)
        {
            this.logger.WriteLine("≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡");
            this.logger.WriteLine("Generating {0}", name);
            this.logger.WriteLine("≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡≡");
        }
    }

    [Fact]
    public void OpensMetadataForSharedReading()
    {
        using FileStream competingReader = File.OpenRead(MetadataPath);
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("CreateFile", CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(TFMDataNoNetFx35MemberData))]
    public void MiniDumpWriteDump_AllOptionalPointerParametersAreOptional(string tfm)
    {
        // We split on TFMs because the generated code is slightly different depending on TFM.
        this.compilation = this.starterCompilations[tfm].WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.GenerateApi("MiniDumpWriteDump");

        MethodDeclarationSyntax externMethod = Assert.Single(this.FindGeneratedMethod("MiniDumpWriteDump"), m => !m.Modifiers.Any(SyntaxKind.ExternKeyword));
        Assert.All(externMethod.ParameterList.Parameters.Reverse().Take(3), p => Assert.IsType<NullableTypeSyntax>(p.Type));
    }

    [Fact]
    public void ContainsIllegalCharactersForAPIName_InvisibleCharacters()
    {
        // You can't see them, but there are invisible hyphens in this name.
        // Copy-paste from learn.microsoft.com has been known to include these invisible characters and break matching in NativeMethods.txt.
        Assert.True(Generator.ContainsIllegalCharactersForAPIName("SHGet­Known­Folder­Item"));
    }

    [Fact]
    public void ContainsIllegalCharactersForAPIName_DisallowedVisibleCharacters()
    {
        Assert.True(Generator.ContainsIllegalCharactersForAPIName("Method-1"));
    }

    [Fact]
    public void ContainsIllegalCharactersForAPIName_LegalNames()
    {
        Assert.False(Generator.ContainsIllegalCharactersForAPIName("SHGetKnownFolderItem"));
        Assert.False(Generator.ContainsIllegalCharactersForAPIName("Method1"));
        Assert.False(Generator.ContainsIllegalCharactersForAPIName("Method_1"));
        Assert.False(Generator.ContainsIllegalCharactersForAPIName("Qualified.Name"));
    }

    [Fact]
    public void ContainsIllegalCharactersForAPIName_AllActualAPINames()
    {
        using FileStream metadataStream = File.OpenRead(MetadataPath);
        using System.Reflection.PortableExecutable.PEReader peReader = new(metadataStream);
        MetadataReader metadataReader = peReader.GetMetadataReader();
        foreach (MethodDefinitionHandle methodDefHandle in metadataReader.MethodDefinitions)
        {
            MethodDefinition methodDef = metadataReader.GetMethodDefinition(methodDefHandle);
            string methodName = metadataReader.GetString(methodDef.Name);
            Assert.False(Generator.ContainsIllegalCharactersForAPIName(methodName), methodName);
        }

        foreach (TypeDefinitionHandle typeDefHandle in metadataReader.TypeDefinitions)
        {
            TypeDefinition typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            string typeName = metadataReader.GetString(typeDef.Name);
            if (typeName == "<Module>")
            {
                // Skip this special one.
                continue;
            }

            Assert.False(Generator.ContainsIllegalCharactersForAPIName(typeName), typeName);
        }

        foreach (FieldDefinitionHandle fieldDefHandle in metadataReader.FieldDefinitions)
        {
            FieldDefinition fieldDef = metadataReader.GetFieldDefinition(fieldDefHandle);
            string fieldName = metadataReader.GetString(fieldDef.Name);
            Assert.False(Generator.ContainsIllegalCharactersForAPIName(fieldName), fieldName);
        }
    }

    [Fact]
    public void ParametersIncludeSizeParamIndex()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("IEnumSearchScopeRules", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics();

        MethodDeclarationSyntax nextMethod = this.FindGeneratedMethod("Next").Single(m => !m.Modifiers.Any(SyntaxKind.StaticKeyword));
        AttributeSyntax marshalAsAttribute = nextMethod.ParameterList.Parameters[1].AttributeLists.SelectMany(al => al.Attributes).Single(a => a.Name.ToString() == "MarshalAs");
        AttributeArgumentSyntax? sizeParamIndex = marshalAsAttribute.ArgumentList?.Arguments.Single(a => a.NameEquals?.Name.ToString() == nameof(MarshalAsAttribute.SizeParamIndex));
        Assert.NotNull(sizeParamIndex);
        Assert.Equal("0", ((LiteralExpressionSyntax)sizeParamIndex!.Expression).Token.ValueText);
    }

    [Fact]
    public void SeekOriginEnumPreferred()
    {
        this.GenerateApi("IStream");

        MethodDeclarationSyntax seekMethod = Assert.Single(this.FindGeneratedMethod("Seek"));
        QualifiedNameSyntax seekParamType = Assert.IsType<QualifiedNameSyntax>(seekMethod.ParameterList.Parameters[1].Type);
        Assert.Equal(nameof(SeekOrigin), seekParamType.Right.Identifier.ValueText);
    }
}
