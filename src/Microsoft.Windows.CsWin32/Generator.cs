// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// The core of the source generator.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplayString) + ",nq}")]
public partial class Generator : IGenerator, IDisposable
{
    private readonly TypeSyntaxSettings generalTypeSettings;
    private readonly TypeSyntaxSettings fieldTypeSettings;
    private readonly TypeSyntaxSettings delegateSignatureTypeSettings;
    private readonly TypeSyntaxSettings enumTypeSettings;
    private readonly TypeSyntaxSettings fieldOfHandleTypeDefTypeSettings;
    private readonly TypeSyntaxSettings externSignatureTypeSettings;
    private readonly TypeSyntaxSettings externReleaseSignatureTypeSettings;
    private readonly TypeSyntaxSettings comSignatureTypeSettings;
    private readonly TypeSyntaxSettings extensionMethodSignatureTypeSettings;
    private readonly TypeSyntaxSettings functionPointerTypeSettings;
    private readonly TypeSyntaxSettings errorMessageTypeSettings;

    private readonly ClassDeclarationSyntax comHelperClass;

    /// <summary>
    /// The struct with one type parameter used to represent a variable-length inline array.
    /// </summary>
    private readonly StructDeclarationSyntax variableLengthInlineArrayStruct1;

    /// <summary>
    /// The struct with two type parameters used to represent a variable-length inline array.
    /// This is useful when the exposed type parameter is C# unmanaged but runtime unblittable (i.e. <see langword="bool" /> and <see langword="char" />).
    /// </summary>
    private readonly StructDeclarationSyntax variableLengthInlineArrayStruct2;

    private readonly Dictionary<string, IReadOnlyList<ISymbol>> findTypeSymbolIfAlreadyAvailableCache = new(StringComparer.Ordinal);
    private readonly List<string> nameExclusions = new();
    private readonly List<string> fullNameExclusions = new();
    private readonly List<string> wildCardExclusions = new();
    private readonly MetadataFile.Rental metadataReader;
    private readonly GeneratorOptions options;
    private readonly CSharpCompilation? compilation;
    private readonly CSharpParseOptions? parseOptions;
    private readonly bool comIIDInterfacePredefined;
    private readonly bool getDelegateForFunctionPointerGenericExists;
    private readonly GeneratedCode committedCode = new();
    private readonly GeneratedCode volatileCode;
    private readonly IdentifierNameSyntax methodsAndConstantsClassName;
    private readonly HashSet<string> injectedPInvokeHelperMethods = new();
    private readonly HashSet<string> injectedPInvokeMacros = new();
    private readonly Dictionary<TypeDefinitionHandle, bool> managedTypesCheck = new();
    private readonly Dictionary<TypeDefinitionHandle, bool> structTypesCheck = new();
    private MethodDeclarationSyntax? sliceAtNullMethodDecl;

    static Generator()
    {
        if (!TryFetchTemplate("PInvokeClassHelperMethods", null, out MemberDeclarationSyntax? member))
        {
            throw new GenerationFailedException("Missing embedded resource.");
        }

        PInvokeHelperMethods = ((ClassDeclarationSyntax)member).Members.OfType<MethodDeclarationSyntax>().ToDictionary(m => m.Identifier.ValueText, m => m);

        if (!TryFetchTemplate("PInvokeClassMacros", null, out member))
        {
            throw new GenerationFailedException("Missing embedded resource.");
        }

        Win32SdkMacros = ((ClassDeclarationSyntax)member).Members.OfType<MethodDeclarationSyntax>().ToDictionary(m => m.Identifier.ValueText, m => m);

        FetchTemplate("IVTable", null, out IVTableInterface);
        FetchTemplate("IVTable`2", null, out IVTableGenericInterface);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Generator"/> class.
    /// </summary>
    /// <param name="metadataLibraryPath">The path to the winmd metadata to generate APIs from.</param>
    /// <param name="docs">The API docs to include in the generated code.</param>
    /// <param name="additionalAppLocalLibraries">The library file names (e.g. some.dll) that should be allowed as app-local.</param>
    /// <param name="options">Options that influence the result of generation.</param>
    /// <param name="compilation">The compilation that the generated code will be added to.</param>
    /// <param name="parseOptions">The parse options that will be used for the generated code.</param>
    public Generator(string metadataLibraryPath, Docs? docs, IEnumerable<string> additionalAppLocalLibraries, GeneratorOptions options, CSharpCompilation? compilation = null, CSharpParseOptions? parseOptions = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        MetadataFile metadataFile = MetadataCache.Default.GetMetadataFile(metadataLibraryPath);
        this.SignatureHandleProvider = new(this);
        this.MetadataIndex = metadataFile.GetMetadataIndex(compilation?.Options.Platform ?? Platform.AnyCpu);
        this.metadataReader = metadataFile.GetMetadataReader();

        this.ApiDocs = docs;

        this.AppLocalLibraries = new(BuiltInAppLocalLibraries, StringComparer.OrdinalIgnoreCase);
        this.AppLocalLibraries.UnionWith(additionalAppLocalLibraries);

        this.options = options;
        this.options.Validate();
        this.compilation = compilation;
        this.parseOptions = parseOptions;
        this.volatileCode = new(this.committedCode);

        // UnscopedRefAttribute may be emitted to work on downlevel *runtimes*, but we can't use it
        // on downlevel *compilers*. Only .NET 8+ SDK compilers support it. Since we cannot detect
        // compiler version, we use language version instead.
        this.canUseUnscopedRef = this.parseOptions?.LanguageVersion >= (LanguageVersion)1100; // C# 11.0

        this.canUseSpan = this.compilation?.GetTypeByMetadataName(typeof(Span<>).FullName) is not null;
        this.canCallCreateSpan = this.compilation?.GetTypeByMetadataName(typeof(MemoryMarshal).FullName)?.GetMembers("CreateSpan").Any() is true;
        this.canUseUnsafeAsRef = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("Add").Any() is true;
        this.canUseUnsafeAdd = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("AsRef").Any() is true;
        this.canUseUnsafeNullRef = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("NullRef").Any() is true;
        this.canUseUnsafeSkipInit = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("SkipInit").Any() is true;
        this.canUseUnmanagedCallersOnlyAttribute = this.FindTypeSymbolsIfAlreadyAvailable("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute").Count > 0;
        this.canUseSetLastPInvokeError = this.compilation?.GetTypeByMetadataName("System.Runtime.InteropServices.Marshal")?.GetMembers("GetLastSystemError").IsEmpty is false;
        this.unscopedRefAttributePredefined = this.FindTypeSymbolIfAlreadyAvailable("System.Diagnostics.CodeAnalysis.UnscopedRefAttribute") is not null;
        this.overloadResolutionPriorityAttributePredefined = this.FindTypeSymbolIfAlreadyAvailable("System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute") is not null;
        this.runtimeFeatureClass = (INamedTypeSymbol?)this.FindTypeSymbolIfAlreadyAvailable("System.Runtime.CompilerServices.RuntimeFeature");
        this.comIIDInterfacePredefined = this.FindTypeSymbolIfAlreadyAvailable($"{this.Namespace}.{IComIIDGuidInterfaceName}") is not null;
        this.getDelegateForFunctionPointerGenericExists = this.compilation?.GetTypeByMetadataName(typeof(Marshal).FullName)?.GetMembers(nameof(Marshal.GetDelegateForFunctionPointer)).Any(m => m is IMethodSymbol { IsGenericMethod: true }) is true;
        this.generateDefaultDllImportSearchPathsAttribute = this.compilation?.GetTypeByMetadataName(typeof(DefaultDllImportSearchPathsAttribute).FullName) is object;
        this.canUseIPropertyValue = this.compilation?.GetTypeByMetadataName("Windows.Foundation.IPropertyValue")?.DeclaredAccessibility == Accessibility.Public;
        this.canUseComVariant = this.compilation?.GetTypeByMetadataName("System.Runtime.InteropServices.Marshalling.ComVariant") is not null;
        this.canUseMemberFunctionCallingConvention = this.compilation?.GetTypeByMetadataName("System.Runtime.CompilerServices.CallConvMemberFunction") is not null;
        this.canUseMarshalInitHandle = this.compilation?.GetTypeByMetadataName(typeof(Marshal).FullName)?.GetMembers("InitHandle").Length > 0;
        if (this.FindTypeSymbolIfAlreadyAvailable("System.Runtime.Versioning.SupportedOSPlatformAttribute") is { } attribute)
        {
            this.generateSupportedOSPlatformAttributes = true;
            AttributeData usageAttribute = attribute.GetAttributes().Single(att => att.AttributeClass?.Name == nameof(AttributeUsageAttribute));
            var targets = (AttributeTargets)usageAttribute.ConstructorArguments[0].Value!;
            this.generateSupportedOSPlatformAttributesOnInterfaces = (targets & AttributeTargets.Interface) == AttributeTargets.Interface;
        }

        // We use source generators if we are in marshaling mode and the user has enabled them.
        this.useSourceGenerators = this.options.AllowMarshaling && (this.options.ComInterop.UseComSourceGenerators ?? false);

        // GeneratedComInterface doesn't support properties yet https://github.com/dotnet/runtime/issues/96502
        this.canDeclareProperties = !this.useSourceGenerators;

        // When runtime marshaling is disabled, native functions can't use out parameters.
        this.canMarshalNativeDelegateParams = !this.useSourceGenerators;

        // Convert some of our CanUse fields to preprocessor symbols so our templates can use them.
        if (this.parseOptions is not null)
        {
            List<string> extraSymbols = new();
            AddSymbolIf(this.canUseSpan, "canUseSpan");
            AddSymbolIf(this.canCallCreateSpan, "canCallCreateSpan");
            AddSymbolIf(this.canUseUnsafeAsRef, "canUseUnsafeAsRef");
            AddSymbolIf(this.canUseUnsafeAdd, "canUseUnsafeAdd");
            AddSymbolIf(this.canUseUnsafeNullRef, "canUseUnsafeNullRef");
            AddSymbolIf(compilation?.GetTypeByMetadataName("System.Drawing.Point") is not null, "canUseSystemDrawing");
            AddSymbolIf(this.IsFeatureAvailable(Feature.InterfaceStaticMembers), "canUseInterfaceStaticMembers");
            AddSymbolIf(this.canUseUnscopedRef, "canUseUnscopedRef");

            if (extraSymbols.Count > 0)
            {
                this.parseOptions = this.parseOptions.WithPreprocessorSymbols(this.parseOptions.PreprocessorSymbolNames.Concat(extraSymbols));
            }

            void AddSymbolIf(bool condition, string symbol)
            {
                if (condition)
                {
                    extraSymbols.Add(symbol);
                }
            }
        }

        bool useComInterfaces = options.AllowMarshaling;
        this.generalTypeSettings = new TypeSyntaxSettings(
            this,
            PreferNativeInt: this.LanguageVersion >= LanguageVersion.CSharp9,
            PreferMarshaledTypes: false,
            AllowMarshaling: options.AllowMarshaling,
            QualifyNames: false);
        this.fieldTypeSettings = this.generalTypeSettings with { QualifyNames = true, IsField = true };
        this.delegateSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true };
        this.enumTypeSettings = this.generalTypeSettings;
        this.fieldOfHandleTypeDefTypeSettings = this.generalTypeSettings with { PreferNativeInt = false };
        this.externSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true, PreferMarshaledTypes = options.AllowMarshaling };
        this.externReleaseSignatureTypeSettings = this.externSignatureTypeSettings with { PreferNativeInt = false, PreferMarshaledTypes = false };
        this.comSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true, PreferInOutRef = options.AllowMarshaling };
        this.extensionMethodSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true };
        this.functionPointerTypeSettings = this.generalTypeSettings with { QualifyNames = true, AvoidWinmdRootAlias = true, AllowMarshaling = false };
        this.errorMessageTypeSettings = this.generalTypeSettings with { QualifyNames = true, Generator = null }; // Avoid risk of infinite recursion from errors in ToTypeSyntax

        this.methodsAndConstantsClassName = IdentifierName(options.ClassName);

        FetchTemplate("ComHelpers", this, out this.comHelperClass);
        FetchTemplate("VariableLengthInlineArray`1", this, out this.variableLengthInlineArrayStruct1);
        FetchTemplate("VariableLengthInlineArray`2", this, out this.variableLengthInlineArrayStruct2);
    }

    internal enum GeneratingElement
    {
        /// <summary>
        /// Any other member that isn't otherwise enumerated.
        /// </summary>
        Other,

        /// <summary>
        /// A member on a COM interface that is actually being generated as an interface (as opposed to a struct for no-marshal COM).
        /// </summary>
        InterfaceMember,

        /// <summary>
        /// A member on a COM interface that is declared as a struct instead of an interface to avoid the marshaler.
        /// </summary>
        InterfaceAsStructMember,

        /// <summary>
        /// A delegate.
        /// </summary>
        Delegate,

        /// <summary>
        /// An extern, static method.
        /// </summary>
        ExternMethod,

        /// <summary>
        /// A property on a COM interface or struct.
        /// </summary>
        Property,

        /// <summary>
        /// A field on a struct.
        /// </summary>
        Field,

        /// <summary>
        /// A constant value.
        /// </summary>
        Constant,

        /// <summary>
        /// A function pointer.
        /// </summary>
        FunctionPointer,

        /// <summary>
        /// An enum value.
        /// </summary>
        EnumValue,

        /// <summary>
        /// A friendly overload.
        /// </summary>
        FriendlyOverload,

        /// <summary>
        /// A member on a helper class (e.g. a SafeHandle-derived class).
        /// </summary>
        HelperClassMember,

        /// <summary>
        /// A member of a struct that does <em>not</em> stand for a COM interface.
        /// </summary>
        StructMember,
    }

    private enum Feature
    {
        /// <summary>
        /// Indicates that interfaces can declare static members. This requires at least .NET 7 and C# 11.
        /// </summary>
        InterfaceStaticMembers,
    }

    internal ImmutableDictionary<string, string> BannedAPIs => GetBannedAPIs(this.options);

    internal SuperGenerator? SuperGenerator { get; set; }

    /// <summary>
    /// Gets the Windows.Win32 generator.
    /// </summary>
    internal Generator MainGenerator
    {
        get
        {
            if (this.IsWin32Sdk || this.SuperGenerator is null)
            {
                return this;
            }

            if (this.SuperGenerator.TryGetGenerator("Windows.Win32", out Generator? generator))
            {
                return generator;
            }

            throw new InvalidOperationException("Unable to find Windows.Win32 generator.");
        }
    }

    internal GeneratorOptions Options => this.options;

    internal string InputAssemblyName => this.MetadataIndex.MetadataName;

    internal MetadataIndex MetadataIndex { get; }

    internal SignatureHandleProvider SignatureHandleProvider { get; }

    internal MetadataReader Reader => this.metadataReader.Value;

    internal LanguageVersion LanguageVersion => this.parseOptions?.LanguageVersion ?? LanguageVersion.CSharp9;

    /// <summary>
    /// Gets the default generation context to use.
    /// </summary>
    internal Context DefaultContext => new() { AllowMarshaling = this.options.AllowMarshaling };

    private HashSet<string> AppLocalLibraries { get; }

    private bool WideCharOnly => this.options.WideCharOnly;

    private string Namespace => this.MetadataIndex.CommonNamespace;

    private SyntaxKind Visibility => this.options.Public ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword;

    private bool IsWin32Sdk => string.Equals(this.MetadataIndex.MetadataName, "Windows.Win32", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<MemberDeclarationSyntax> NamespaceMembers
    {
        get
        {
            IEnumerable<IGrouping<string, MemberDeclarationSyntax>> members = this.committedCode.MembersByModule;
            IEnumerable<MemberDeclarationSyntax> result = Enumerable.Empty<MemberDeclarationSyntax>();
            int i = 0;
            foreach (IGrouping<string, MemberDeclarationSyntax> entry in members)
            {
                ClassDeclarationSyntax partialClass = DeclarePInvokeClass(entry.Key)
                    .AddMembers(entry.ToArray())
                    .WithLeadingTrivia(ParseLeadingTrivia(string.Format(CultureInfo.InvariantCulture, PartialPInvokeContentComment, entry.Key)));
                if (i == 0)
                {
                    partialClass = partialClass
                        .WithoutLeadingTrivia()
                        .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute))
                        .WithLeadingTrivia(partialClass.GetLeadingTrivia());
                }

                result = result.Concat(new MemberDeclarationSyntax[] { partialClass });
                i++;
            }

            ClassDeclarationSyntax macrosPartialClass = DeclarePInvokeClass("Macros")
                .AddMembers(this.committedCode.Macros.ToArray())
                .WithLeadingTrivia(ParseLeadingTrivia(PartialPInvokeMacrosContentComment));
            if (macrosPartialClass.Members.Count > 0)
            {
                result = result.Concat(new MemberDeclarationSyntax[] { macrosPartialClass });
            }

            ClassDeclarationSyntax DeclarePInvokeClass(string fileNameKey) => ClassDeclaration(Identifier(this.options.ClassName))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword))
                .WithAdditionalAnnotations(new SyntaxAnnotation(SimpleFileNameAnnotation, $"{this.options.ClassName}.{fileNameKey}"));

            result = result.Concat(this.committedCode.GeneratedTypes);

            ClassDeclarationSyntax inlineArrayIndexerExtensionsClass = this.DeclareInlineArrayIndexerExtensionsClass();
            if (inlineArrayIndexerExtensionsClass.Members.Count > 0)
            {
                result = result.Concat(new MemberDeclarationSyntax[] { inlineArrayIndexerExtensionsClass });
            }

            result = result.Concat(this.committedCode.ComInterfaceExtensions);

            if (this.committedCode.TopLevelFields.Any())
            {
                result = result.Concat(new MemberDeclarationSyntax[] { this.DeclareConstantDefiningClass() });
            }

            return result;
        }
    }

    private string DebuggerDisplayString => $"Generator: {this.InputAssemblyName}";

    /// <summary>
    /// Tests whether a string contains characters that do not belong in an API name.
    /// </summary>
    /// <param name="apiName">The user-supplied string that was expected to match some API name.</param>
    /// <returns><see langword="true"/> if the string contains characters that are likely mistakenly included and causing a mismatch; <see langword="false"/> otherwise.</returns>
    public static bool ContainsIllegalCharactersForAPIName(string apiName)
    {
        if (apiName is null)
        {
            throw new ArgumentNullException(nameof(apiName));
        }

        for (int i = 0; i < apiName.Length; i++)
        {
            char ch = apiName[i];
            bool allowed = false;
            allowed |= char.IsLetterOrDigit(ch);
            allowed |= ch == '_';
            allowed |= ch == '.'; // for qualified name searches

            if (!allowed)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public void GenerateAll(CancellationToken cancellationToken)
    {
        this.GenerateAllExternMethods(cancellationToken);

        // Also generate all structs/enum types too, even if not referenced by a method,
        // since some methods use `void*` types and require structs at runtime.
        this.GenerateAllInteropTypes(cancellationToken);

        this.GenerateAllConstants(cancellationToken);

        this.GenerateAllMacros(cancellationToken);
    }

    /// <inheritdoc/>
    public bool TryGenerate(string apiNameOrModuleWildcard, out IReadOnlyCollection<string> preciseApi, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiNameOrModuleWildcard))
        {
            throw new ArgumentException("API cannot be null or empty.", nameof(apiNameOrModuleWildcard));
        }

        if (apiNameOrModuleWildcard.EndsWith(".*", StringComparison.Ordinal))
        {
            if (this.TryGenerateAllExternMethods(apiNameOrModuleWildcard.Substring(0, apiNameOrModuleWildcard.Length - 2), cancellationToken))
            {
                preciseApi = ImmutableList.Create(apiNameOrModuleWildcard);
                return true;
            }
            else
            {
                preciseApi = ImmutableList<string>.Empty;
                return false;
            }
        }
        else if (apiNameOrModuleWildcard.EndsWith("*", StringComparison.Ordinal))
        {
            if (this.TryGenerateConstants(apiNameOrModuleWildcard))
            {
                preciseApi = ImmutableList.Create(apiNameOrModuleWildcard);
                return true;
            }
            else
            {
                preciseApi = ImmutableList<string>.Empty;
                return false;
            }
        }
        else
        {
            bool result = this.TryGenerateNamespace(apiNameOrModuleWildcard, out preciseApi);
            if (result || preciseApi.Count > 1)
            {
                return result;
            }

            result = this.TryGenerateExternMethod(apiNameOrModuleWildcard, out preciseApi);
            if (result || preciseApi.Count > 1)
            {
                return result;
            }

            result = this.TryGenerateType(apiNameOrModuleWildcard, out preciseApi);
            if (result || preciseApi.Count > 1)
            {
                return result;
            }

            result = this.TryGenerateConstant(apiNameOrModuleWildcard, out preciseApi);
            if (result || preciseApi.Count > 1)
            {
                return result;
            }

            result = this.TryGenerateMacro(apiNameOrModuleWildcard, out preciseApi);
            if (result || preciseApi.Count > 1)
            {
                return result;
            }

            return false;
        }
    }

    /// <summary>
    /// Generates all APIs within a given namespace, and their dependencies.
    /// </summary>
    /// <param name="namespace">The namespace to generate APIs for.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="namespace"/> matched on.</param>
    /// <returns><see langword="true"/> if a matching namespace was found; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateNamespace(string @namespace, out IReadOnlyCollection<string> preciseApi)
    {
        if (@namespace is null)
        {
            throw new ArgumentNullException(nameof(@namespace));
        }

        NamespaceMetadata? metadata;
        if (!this.MetadataIndex.MetadataByNamespace.TryGetValue(@namespace, out metadata))
        {
            // Fallback to case insensitive search if it looks promising to do so.
            if (@namespace.StartsWith(this.MetadataIndex.CommonNamespace, StringComparison.OrdinalIgnoreCase))
            {
                foreach (KeyValuePair<string, NamespaceMetadata> item in this.MetadataIndex.MetadataByNamespace)
                {
                    if (string.Equals(item.Key, @namespace, StringComparison.OrdinalIgnoreCase))
                    {
                        @namespace = item.Key;
                        metadata = item.Value;
                        break;
                    }
                }
            }
        }

        if (metadata is object)
        {
            this.volatileCode.GenerationTransaction(delegate
            {
                foreach (KeyValuePair<string, MethodDefinitionHandle> method in metadata.Methods)
                {
                    this.RequestExternMethod(method.Value);
                }

                foreach (KeyValuePair<string, TypeDefinitionHandle> type in metadata.Types)
                {
                    this.RequestInteropType(type.Value, this.DefaultContext);
                }

                foreach (KeyValuePair<string, FieldDefinitionHandle> field in metadata.Fields)
                {
                    this.RequestConstant(field.Value);
                }
            });

            preciseApi = ImmutableList.Create(@namespace);
            return true;
        }

        preciseApi = ImmutableList<string>.Empty;
        return false;
    }

    /// <inheritdoc/>
    public void GenerateAllMacros(CancellationToken cancellationToken)
    {
        if (!this.IsWin32Sdk)
        {
            // We only have macros to generate for the main SDK.
            return;
        }

        foreach (KeyValuePair<string, MethodDeclarationSyntax> macro in Win32SdkMacros.OrderBy(x => x.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                this.volatileCode.GenerationTransaction(delegate
                {
                    this.RequestMacro(macro.Value);
                });
            }
            catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
            {
                // Something transitively required for this field is not available for this platform, so skip this method.
            }
        }
    }

    /// <inheritdoc/>
    public void GenerateAllInteropTypes(CancellationToken cancellationToken)
    {
        var sortedInteropTypes = this.Reader.TypeDefinitions.OrderBy(x => this.Reader.GetString(this.Reader.GetTypeDefinition(x).Namespace))
                                                           .ThenBy(x => this.Reader.GetString(this.Reader.GetTypeDefinition(x).Name));

        foreach (TypeDefinitionHandle typeDefinitionHandle in sortedInteropTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefinitionHandle);
            if (typeDef.BaseType.IsNil && (typeDef.Attributes & TypeAttributes.Interface) != TypeAttributes.Interface)
            {
                continue;
            }

            if (this.Reader.StringComparer.Equals(typeDef.Namespace, InteropDecorationNamespace))
            {
                // Ignore the attributes that describe the metadata.
                continue;
            }

            if (this.IsCompatibleWithPlatform(typeDef.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestInteropType(typeDefinitionHandle, this.DefaultContext);
                    });
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this type is not available for this platform, so skip this method.
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGenerateType(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
    {
        if (possiblyQualifiedName is null)
        {
            throw new ArgumentNullException(nameof(possiblyQualifiedName));
        }

        TrySplitPossiblyQualifiedName(possiblyQualifiedName, out string? typeNamespace, out string typeName);
        var matchingTypeHandles = new List<TypeDefinitionHandle>();
        IEnumerable<NamespaceMetadata>? namespaces = this.GetNamespacesToSearch(typeNamespace);
        bool foundApiWithMismatchedPlatform = false;

        foreach (NamespaceMetadata? nsMetadata in namespaces)
        {
            if (nsMetadata.Types.TryGetValue(typeName, out TypeDefinitionHandle handle))
            {
                matchingTypeHandles.Add(handle);
            }
            else if (nsMetadata.TypesForOtherPlatform.Contains(typeName))
            {
                foundApiWithMismatchedPlatform = true;
            }
        }

        if (matchingTypeHandles.Count == 1)
        {
            this.volatileCode.GenerationTransaction(delegate
            {
                this.RequestInteropType(matchingTypeHandles[0], this.DefaultContext);
            });

            TypeDefinition td = this.Reader.GetTypeDefinition(matchingTypeHandles[0]);
            preciseApi = ImmutableList.Create($"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(td.Name)}");
            return true;
        }
        else if (matchingTypeHandles.Count > 1)
        {
            preciseApi = ImmutableList.CreateRange(
                matchingTypeHandles.Select(h =>
                {
                    TypeDefinition td = this.Reader.GetTypeDefinition(h);
                    return $"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(td.Name)}";
                }));
            return false;
        }

        if (this.InputAssemblyName.Equals("Windows.Win32", StringComparison.OrdinalIgnoreCase) && SpecialTypeDefNames.Contains(typeName))
        {
            string? fullyQualifiedName = null;
            this.volatileCode.GenerationTransaction(() => this.RequestSpecialTypeDefStruct(typeName, out fullyQualifiedName));
            preciseApi = ImmutableList.Create(fullyQualifiedName!);
            return true;
        }

        if (foundApiWithMismatchedPlatform)
        {
            throw new PlatformIncompatibleException($"The requested API ({possiblyQualifiedName}) was found but is not available given the target platform ({this.compilation?.Options.Platform}).");
        }

        preciseApi = ImmutableList<string>.Empty;
        return false;
    }

    /// <summary>
    /// Generate code for the named macro, if it is recognized.
    /// </summary>
    /// <param name="macroName">The name of the macro. Never qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="macroName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the macro generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateMacro(string macroName, out IReadOnlyCollection<string> preciseApi)
    {
        if (macroName is null)
        {
            throw new ArgumentNullException(nameof(macroName));
        }

        if (!this.IsWin32Sdk || !Win32SdkMacros.TryGetValue(macroName, out MethodDeclarationSyntax? macro))
        {
            preciseApi = Array.Empty<string>();
            return false;
        }

        this.volatileCode.GenerationTransaction(delegate
        {
            this.RequestMacro(macro);
        });

        preciseApi = ImmutableList.Create(macroName);
        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSuggestions(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        // Trim suffixes off the name.
        var suffixes = new List<string> { "A", "W", "32", "64", "Ex" };
        foreach (string suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - suffix.Length);
            }
        }

        // We should match on any API for which the given string is a substring.
        List<string> suggestions = new();
        foreach (NamespaceMetadata nsMetadata in this.MetadataIndex.MetadataByNamespace.Values)
        {
            foreach (string candidate in nsMetadata.Fields.Keys.Concat(nsMetadata.Types.Keys).Concat(nsMetadata.Methods.Keys))
            {
                if (candidate.Contains(name))
                {
                    suggestions.Add(candidate);
                }
            }
        }

        return suggestions;
    }

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<string, CompilationUnitSyntax>> GetCompilationUnits(CancellationToken cancellationToken)
    {
        if (this.committedCode.IsEmpty)
        {
            return ImmutableDictionary<string, CompilationUnitSyntax>.Empty;
        }

        NamespaceDeclarationSyntax? starterNamespace = NamespaceDeclaration(ParseName(this.Namespace));

        // .g.cs because the resulting files are not user-created.
        const string FilenamePattern = "{0}.g.cs";
        Dictionary<string, CompilationUnitSyntax> results = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<MemberDeclarationSyntax> GroupMembersByNamespace(IEnumerable<MemberDeclarationSyntax> members)
        {
            return members.GroupBy(member =>
                member.HasAnnotations(NamespaceContainerAnnotation) ? member.GetAnnotations(NamespaceContainerAnnotation).Single().Data : null)
                .SelectMany(nsContents =>
                    nsContents.Key is object
                        ? new MemberDeclarationSyntax[] { NamespaceDeclaration(ParseName(nsContents.Key)).AddMembers(nsContents.ToArray()) }
                        : nsContents.ToArray());
        }

        if (this.options.EmitSingleFile == true)
        {
            CompilationUnitSyntax file = CompilationUnit()
                .AddMembers(starterNamespace.AddMembers(GroupMembersByNamespace(this.NamespaceMembers).ToArray()))
                .AddMembers(this.committedCode.GeneratedTopLevelTypes.ToArray());
            results.Add(
                string.Format(CultureInfo.InvariantCulture, FilenamePattern, "NativeMethods"),
                file);
        }
        else
        {
            foreach (MemberDeclarationSyntax topLevelType in this.committedCode.GeneratedTopLevelTypes)
            {
                string typeName = topLevelType.DescendantNodesAndSelf().OfType<BaseTypeDeclarationSyntax>().First().Identifier.ValueText;
                results.Add(
                    string.Format(CultureInfo.InvariantCulture, FilenamePattern, typeName),
                    CompilationUnit().AddMembers(topLevelType));
            }

            IEnumerable<IGrouping<string?, MemberDeclarationSyntax>>? membersByFile = this.NamespaceMembers.GroupBy(
                member => member.HasAnnotations(SimpleFileNameAnnotation)
                        ? member.GetAnnotations(SimpleFileNameAnnotation).Single().Data
                        : member switch
                        {
                            ClassDeclarationSyntax classDecl => classDecl.Identifier.ValueText,
                            StructDeclarationSyntax structDecl => structDecl.Identifier.ValueText,
                            InterfaceDeclarationSyntax ifaceDecl => ifaceDecl.Identifier.ValueText,
                            EnumDeclarationSyntax enumDecl => enumDecl.Identifier.ValueText,
                            DelegateDeclarationSyntax delegateDecl => "Delegates", // group all delegates in one file
                            _ => throw new NotSupportedException("Unsupported member type: " + member.GetType().Name),
                        },
                StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string?, MemberDeclarationSyntax>? fileSimpleName in membersByFile)
            {
                try
                {
                    CompilationUnitSyntax file = CompilationUnit()
                        .AddMembers(starterNamespace.AddMembers(GroupMembersByNamespace(fileSimpleName).ToArray()));
                    results.Add(
                        string.Format(CultureInfo.InvariantCulture, FilenamePattern, fileSimpleName.Key),
                        file);
                }
                catch (ArgumentException ex)
                {
                    throw new GenerationFailedException($"Failed adding \"{fileSimpleName.Key}\".", ex);
                }
            }
        }

        var usingDirectives = new List<UsingDirectiveSyntax>
        {
            UsingDirective(AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), IdentifierName(nameof(System)))),
            UsingDirective(AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), IdentifierName(nameof(System) + "." + nameof(System.Diagnostics)))),
            UsingDirective(AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), IdentifierName(nameof(System) + "." + nameof(System.Diagnostics) + "." + nameof(System.Diagnostics.CodeAnalysis)))),
            UsingDirective(ParseName(GlobalNamespacePrefix + SystemRuntimeCompilerServices)),
            UsingDirective(ParseName(GlobalNamespacePrefix + SystemRuntimeInteropServices)),
        };

        if (this.useSourceGenerators)
        {
            usingDirectives.Add(UsingDirective(ParseName(GlobalNamespacePrefix + SystemRuntimeInteropServicesMarshalling)));
        }

        if (this.generateSupportedOSPlatformAttributes)
        {
            usingDirectives.Add(UsingDirective(ParseName(GlobalNamespacePrefix + "System.Runtime.Versioning")));
        }

        usingDirectives.Add(UsingDirective(NameEquals(GlobalWinmdRootNamespaceAlias), ParseName(GlobalNamespacePrefix + this.MetadataIndex.CommonNamespace)));

        var normalizedResults = new Dictionary<string, CompilationUnitSyntax>(StringComparer.OrdinalIgnoreCase);
        results.AsParallel().WithCancellation(cancellationToken).ForAll(kv =>
        {
            CompilationUnitSyntax? compilationUnit = ((CompilationUnitSyntax)kv.Value
                .AddUsings(usingDirectives.ToArray())
                .Accept(new WhitespaceRewriter())!)
                .WithLeadingTrivia(FileHeader);

            lock (normalizedResults)
            {
                normalizedResults.Add(kv.Key, compilationUnit);
            }
        });

        if (this.compilation?.GetTypeByMetadataName("System.Reflection.AssemblyMetadataAttribute") is not null)
        {
            if (this.options.EmitSingleFile == true)
            {
                KeyValuePair<string, CompilationUnitSyntax> originalEntry = normalizedResults.Single();
                normalizedResults[originalEntry.Key] = originalEntry.Value.WithLeadingTrivia().AddAttributeLists(CsWin32StampAttribute).WithLeadingTrivia(originalEntry.Value.GetLeadingTrivia());
            }
            else
            {
                normalizedResults.Add(string.Format(CultureInfo.InvariantCulture, FilenamePattern, "CsWin32Stamp"), CompilationUnit().AddAttributeLists(CsWin32StampAttribute).WithLeadingTrivia(FileHeader));
            }
        }

        if (this.committedCode.NeedsWinRTCustomMarshaler)
        {
            string? marshalerText = FetchTemplateText(WinRTCustomMarshalerClass);
            if (marshalerText == null)
            {
                throw new GenerationFailedException($"Failed to get template for \"{WinRTCustomMarshalerClass}\".");
            }

            SyntaxTree? marshalerContents = SyntaxFactory.ParseSyntaxTree(marshalerText, cancellationToken: cancellationToken);
            if (marshalerContents == null)
            {
                throw new GenerationFailedException($"Failed adding \"{WinRTCustomMarshalerClass}\".");
            }

            CompilationUnitSyntax? compilationUnit = ((CompilationUnitSyntax)marshalerContents.GetRoot(cancellationToken))
                .WithLeadingTrivia(ParseLeadingTrivia(AutoGeneratedHeader));

            normalizedResults.Add(
                string.Format(CultureInfo.InvariantCulture, FilenamePattern, WinRTCustomMarshalerClass),
                compilationUnit);
        }

        return normalizedResults;
    }

    /// <inheritdoc/>
    public void AddGeneratorExclusion(string exclusionLine)
    {
        if (exclusionLine.Contains("."))
        {
            if (exclusionLine.EndsWith(".*", StringComparison.Ordinal))
            {
                this.wildCardExclusions.Add(exclusionLine[..^2]);
            }
            else
            {
                this.fullNameExclusions.Add(exclusionLine);
            }
        }
        else
        {
            this.nameExclusions.Add(exclusionLine);
        }
    }

    internal static ImmutableDictionary<string, string> GetBannedAPIs(GeneratorOptions options) => options.AllowMarshaling ? BannedAPIsWithMarshaling : BannedAPIsWithoutMarshaling;

    /// <summary>
    /// Checks whether an exception was originally thrown because of a target platform incompatibility.
    /// </summary>
    /// <param name="ex">An exception that may be or contain a <see cref="PlatformIncompatibleException"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="ex"/> or an inner exception is a <see cref="PlatformIncompatibleException"/>; otherwise <see langword="false" />.</returns>
    internal static bool IsPlatformCompatibleException(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        return ex is PlatformIncompatibleException || IsPlatformCompatibleException(ex?.InnerException);
    }

    internal static string ReplaceCommonNamespaceWithAlias(Generator? generator, string fullNamespace)
    {
        return generator is object && generator.TryStripCommonNamespace(fullNamespace, out string? stripped) ? (stripped.Length > 0 ? $"{GlobalWinmdRootNamespaceAlias}.{stripped}" : GlobalWinmdRootNamespaceAlias) : $"global::{fullNamespace}";
    }

    internal void RequestComHelpers(Context context)
    {
        if (this.IsWin32Sdk)
        {
            if (!this.IsTypeAlreadyFullyDeclared($"{this.Namespace}.{this.comHelperClass.Identifier.ValueText}"))
            {
                this.RequestInteropType("Windows.Win32.Foundation", "HRESULT", context);
                this.volatileCode.GenerateSpecialType("ComHelpers", () => this.volatileCode.AddSpecialType("ComHelpers", this.comHelperClass));
            }

            if (this.IsFeatureAvailable(Feature.InterfaceStaticMembers) && !context.AllowMarshaling)
            {
                if (!this.IsTypeAlreadyFullyDeclared($"{this.Namespace}.{IVTableInterface.Identifier.ValueText}"))
                {
                    this.volatileCode.GenerateSpecialType("IVTable", () => this.volatileCode.AddSpecialType("IVTable", IVTableInterface));
                }

                if (!this.IsTypeAlreadyFullyDeclared($"{this.Namespace}.{IVTableGenericInterface.Identifier.ValueText}`2"))
                {
                    this.volatileCode.GenerateSpecialType("IVTable`2", () => this.volatileCode.AddSpecialType("IVTable`2", IVTableGenericInterface));
                }

                if (!this.TryGenerate("IUnknown", default))
                {
                    throw new GenerationFailedException("Unable to generate IUnknown.");
                }
            }
        }
        else if (this.SuperGenerator is not null && this.SuperGenerator.TryGetGenerator("Windows.Win32", out Generator? generator))
        {
            generator.volatileCode.GenerationTransaction(delegate
            {
                generator.RequestComHelpers(context);
            });
        }
    }

    internal bool TryStripCommonNamespace(string fullNamespace, [NotNullWhen(true)] out string? strippedNamespace)
    {
        if (fullNamespace.StartsWith(this.MetadataIndex.CommonNamespaceDot, StringComparison.Ordinal))
        {
            strippedNamespace = fullNamespace.Substring(this.MetadataIndex.CommonNamespaceDot.Length);
            return true;
        }
        else if (fullNamespace == this.MetadataIndex.CommonNamespace)
        {
            strippedNamespace = string.Empty;
            return true;
        }

        strippedNamespace = null;
        return false;
    }

    internal void RequestInteropType(string @namespace, string name, Context context)
    {
        // PERF: Skip this search if this namespace/name has already been generated (committed, or still in volatileCode).
        foreach (TypeDefinitionHandle tdh in this.Reader.TypeDefinitions)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            if (this.Reader.StringComparer.Equals(td.Name, name) && this.Reader.StringComparer.Equals(td.Namespace, @namespace))
            {
                this.volatileCode.GenerationTransaction(delegate
                {
                    this.RequestInteropType(tdh, context);
                });

                return;
            }
        }

        throw new GenerationFailedException($"Referenced type \"{@namespace}.{name}\" not found in \"{this.InputAssemblyName}\".");
    }

    internal void RequestInteropType(TypeDefinitionHandle typeDefHandle, Context context)
    {
        this.volatileCode.GenerationTransaction(delegate
        {
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
            if (typeDef.GetDeclaringType() is { IsNil: false } nestingParentHandle)
            {
                // We should only generate this type into its parent type.
                this.RequestInteropType(nestingParentHandle, context);
                return;
            }

            string ns = this.Reader.GetString(typeDef.Namespace);
            if (!this.IsCompatibleWithPlatform(typeDef.GetCustomAttributes()))
            {
                // We've been asked for an interop type that does not apply. This happens because the metadata
                // may use a TypeReferenceHandle or TypeDefinitionHandle to just one of many arch-specific definitions of this type.
                // Try to find the appropriate definition for our target architecture.
                string name = this.Reader.GetString(typeDef.Name);
                NamespaceMetadata namespaceMetadata = this.MetadataIndex.MetadataByNamespace[ns];
                if (!namespaceMetadata.Types.TryGetValue(name, out typeDefHandle) && namespaceMetadata.TypesForOtherPlatform.Contains(name))
                {
                    throw new PlatformIncompatibleException($"Request for type ({ns}.{name}) that is not available given the target platform.");
                }
            }

            bool hasUnmanagedName = this.HasUnmanagedSuffix(this.Reader, typeDef.Name, context.AllowMarshaling, this.IsManagedType(typeDefHandle));
            this.volatileCode.GenerateType(typeDefHandle, hasUnmanagedName, delegate
            {
                if (this.RequestInteropTypeHelper(typeDefHandle, context) is MemberDeclarationSyntax typeDeclaration)
                {
                    if (!this.TryStripCommonNamespace(ns, out string? shortNamespace))
                    {
                        throw new GenerationFailedException("Unexpected namespace: " + ns);
                    }

                    if (shortNamespace.Length > 0)
                    {
                        typeDeclaration = typeDeclaration.WithAdditionalAnnotations(
                            new SyntaxAnnotation(NamespaceContainerAnnotation, shortNamespace));
                    }

                    this.volatileCode.AddInteropType(typeDefHandle, hasUnmanagedName, typeDeclaration);
                }
            });
        });
    }

    internal void RequestInteropType(TypeReferenceHandle typeRefHandle, Context context)
    {
        if (this.TryGetTypeDefHandle(typeRefHandle, out TypeDefinitionHandle typeDefHandle))
        {
            this.RequestInteropType(typeDefHandle, context);
        }
        else
        {
            TypeReference typeRef = this.Reader.GetTypeReference(typeRefHandle);
            if (typeRef.ResolutionScope.Kind == HandleKind.AssemblyReference)
            {
                if (this.SuperGenerator?.TryRequestInteropType(new(this, typeRef), context) is not true)
                {
                    // We can't find the interop among our metadata inputs.
                    // Before we give up and report an error, search for the required type among the compilation's referenced assemblies.
                    string metadataName = $"{this.Reader.GetString(typeRef.Namespace)}.{this.Reader.GetString(typeRef.Name)}";
                    if (this.compilation?.GetTypeByMetadataName(metadataName) is null)
                    {
                        AssemblyReference assemblyRef = this.Reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
                        string scope = this.Reader.GetString(assemblyRef.Name);
                        if (scope != "Windows.Foundation.UniversalApiContract")
                        {
                            throw new GenerationFailedException($"Input metadata file \"{scope}\" has not been provided, or is referenced at a version that is lacking the type \"{metadataName}\".");
                        }
                    }
                }
            }
        }
    }

    internal void RequestMacro(MethodDeclarationSyntax macro)
    {
        this.volatileCode.GenerateMacro(macro.Identifier.ValueText, delegate
        {
            this.volatileCode.AddMacro(macro.Identifier.ValueText, (MethodDeclarationSyntax)this.ElevateVisibility(macro));

            // Generate any additional types that this macro relies on.
            foreach (QualifiedNameSyntax identifier in macro.DescendantNodes().OfType<QualifiedNameSyntax>())
            {
                string identifierString = identifier.ToString();
                if (identifierString.StartsWith(GlobalNamespacePrefix, StringComparison.Ordinal))
                {
                    this.TryGenerateType(identifierString.Substring(GlobalNamespacePrefix.Length));
                }
            }

            // Generate macro dependencies, if any.
            foreach (IdentifierNameSyntax identifier in macro.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                string identifierString = identifier.ToString();
                if (Win32SdkMacros.ContainsKey(identifierString))
                {
                    this.TryGenerateMacro(identifierString, out _);
                }
            }
        });
    }

    internal void GetBaseTypeInfo(TypeDefinition typeDef, out StringHandle baseTypeName, out StringHandle baseTypeNamespace)
    {
        if (typeDef.BaseType.IsNil)
        {
            baseTypeName = default;
            baseTypeNamespace = default;
        }
        else
        {
            switch (typeDef.BaseType.Kind)
            {
                case HandleKind.TypeReference:
                    TypeReference baseTypeRef = this.Reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType);
                    baseTypeName = baseTypeRef.Name;
                    baseTypeNamespace = baseTypeRef.Namespace;
                    break;
                case HandleKind.TypeDefinition:
                    TypeDefinition baseTypeDef = this.Reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
                    baseTypeName = baseTypeDef.Name;
                    baseTypeNamespace = baseTypeDef.Namespace;
                    break;
                default:
                    throw new NotSupportedException("Unsupported base type handle: " + typeDef.BaseType.Kind);
            }
        }
    }

    internal MemberDeclarationSyntax? RequestSpecialTypeDefStruct(string specialName, out string fullyQualifiedName)
    {
        string subNamespace = "Foundation";
        string ns = $"{this.Namespace}.{subNamespace}";
        fullyQualifiedName = $"{ns}.{specialName}";

        if (this.IsTypeAlreadyFullyDeclared(fullyQualifiedName))
        {
            // The type already exists either in this project or a referenced one.
            return null;
        }

        MemberDeclarationSyntax? specialDeclaration = null;
        if (this.InputAssemblyName.Equals("Windows.Win32", StringComparison.OrdinalIgnoreCase))
        {
            this.volatileCode.GenerateSpecialType(specialName, delegate
            {
                switch (specialName)
                {
                    case "PCWSTR":
                    case "PCSTR":
                    case "PCZZSTR":
                    case "PCZZWSTR":
                    case "PZZSTR":
                    case "PZZWSTR":
                        specialDeclaration = this.FetchTemplate($"{specialName}");
                        if (!specialName.StartsWith("PC", StringComparison.Ordinal))
                        {
                            this.TryGenerateType("Windows.Win32.Foundation.PC" + specialName.Substring(1)); // the template references its constant version
                        }
                        else if (specialName.StartsWith("PCZZ", StringComparison.Ordinal))
                        {
                            this.TryGenerateType("Windows.Win32.Foundation.PC" + specialName.Substring(4)); // the template references its single string version
                        }

                        break;
                    default:
                        throw new ArgumentException($"This special name is not recognized: \"{specialName}\".", nameof(specialName));
                }

                if (specialDeclaration is null)
                {
                    throw new GenerationFailedException("Failed to parse template.");
                }

                specialDeclaration = specialDeclaration
                    .WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, subNamespace))
                    .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute));

                this.volatileCode.AddSpecialType(specialName, specialDeclaration);
            });
        }
        else if (this.SuperGenerator?.TryGetGenerator("Windows.Win32", out Generator? win32Generator) is true)
        {
            string? fullyQualifiedNameLocal = null!;
            win32Generator.volatileCode.GenerationTransaction(delegate
            {
                specialDeclaration = win32Generator.RequestSpecialTypeDefStruct(specialName, out fullyQualifiedNameLocal);
            });
            fullyQualifiedName = fullyQualifiedNameLocal;
        }

        return specialDeclaration;
    }

    internal bool HasUnmanagedSuffix(string originalName, bool allowMarshaling, bool isManagedType) => !allowMarshaling && isManagedType && this.options.AllowMarshaling && originalName is not "IUnknown";

    internal bool HasUnmanagedSuffix(MetadataReader reader, StringHandle typeName, bool allowMarshaling, bool isManagedType) => !allowMarshaling && isManagedType && this.options.AllowMarshaling && !reader.StringComparer.Equals(typeName, "IUnknown");

    internal string GetMangledIdentifier(string normalIdentifier, bool allowMarshaling, bool isManagedType) =>
        this.HasUnmanagedSuffix(normalIdentifier, allowMarshaling, isManagedType) ? normalIdentifier + UnmanagedInteropSuffix : normalIdentifier;

    internal Generator GetGeneratorFromReader(MetadataReader reader)
    {
        if (this.SuperGenerator is object)
        {
            return this.SuperGenerator.GetGeneratorFromReader(reader);
        }
        else if (reader == this.Reader)
        {
            return this;
        }

        throw new InvalidOperationException("Encountered a reader not associated with an active generator");
    }

    internal bool IsExcludedName(string fullyQualifiedMetadataName)
    {
        // Check the exclusion lists
        if (this.nameExclusions.Count > 0)
        {
            int fullyQualifiedMetadataNameLastDot = fullyQualifiedMetadataName.LastIndexOf(".");
            string namePortionWithDot = (fullyQualifiedMetadataNameLastDot != -1) ?
                fullyQualifiedMetadataName[fullyQualifiedMetadataNameLastDot..] :
                fullyQualifiedMetadataName;
            foreach (string exclusion in this.nameExclusions)
            {
                if (fullyQualifiedMetadataName.EndsWith(exclusion, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        foreach (string exclusion in this.fullNameExclusions)
        {
            if (fullyQualifiedMetadataName == exclusion)
            {
                return true;
            }
        }

        foreach (string exclusion in this.wildCardExclusions)
        {
            if (fullyQualifiedMetadataName.StartsWith(exclusion, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> if being disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.metadataReader.Dispose();
        }
    }

    /// <summary>
    /// Checks for periods in a name and if found, splits off the last element as the name and considers everything before it to be a namespace.
    /// </summary>
    /// <param name="possiblyQualifiedName">A name or qualified name (e.g. "String" or "System.String").</param>
    /// <param name="namespace">Receives the namespace portion if present in <paramref name="possiblyQualifiedName"/> (e.g. "System"); otherwise <see langword="null"/>.</param>
    /// <param name="name">Receives the name portion from <paramref name="possiblyQualifiedName"/>.</param>
    /// <returns>A value indicating whether a namespace was present in <paramref name="possiblyQualifiedName"/>.</returns>
    private static bool TrySplitPossiblyQualifiedName(string possiblyQualifiedName, [NotNullWhen(true)] out string? @namespace, out string name)
    {
        int nameIdx = possiblyQualifiedName.LastIndexOf('.');
        @namespace = nameIdx >= 0 ? possiblyQualifiedName.Substring(0, nameIdx) : null;
        name = nameIdx >= 0 ? possiblyQualifiedName.Substring(nameIdx + 1) : possiblyQualifiedName;
        return @namespace is object;
    }

    private static NativeArrayInfo DecodeNativeArrayInfoAttribute(CustomAttribute nativeArrayInfoAttribute)
    {
        CustomAttributeValue<TypeSyntax> args = nativeArrayInfoAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        return new NativeArrayInfo
        {
            CountConst = (int?)args.NamedArguments.FirstOrDefault(a => a.Name == "CountConst").Value,
            CountParamIndex = (short?)args.NamedArguments.FirstOrDefault(a => a.Name == "CountParamIndex").Value,
        };
    }

    private static MemorySize DecodeMemorySizeAttribute(CustomAttribute memorySizeAttribute)
    {
        CustomAttributeValue<TypeSyntax> args = memorySizeAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        return new MemorySize
        {
            BytesParamIndex = (short?)args.NamedArguments.FirstOrDefault(a => a.Name == "BytesParamIndex").Value,
        };
    }

    private bool TryGetRenamedMethod(string methodName, [NotNullWhen(true)] out string? newName)
    {
        if (this.WideCharOnly && IsWideFunction(methodName))
        {
            newName = methodName.Substring(0, methodName.Length - 1);
            return !this.GetMethodByName(newName, exactNameMatchOnly: true).HasValue;
        }

        newName = null;
        return false;
    }

    /// <summary>
    /// Checks whether a type with the given name is already defined in the compilation
    /// such that we must (or should) skip generating it ourselves.
    /// </summary>
    /// <param name="fullyQualifiedMetadataName">The fully-qualified metadata name of the type.</param>
    /// <returns><see langword="true"/> if the type should <em>not</em> be emitted; <see langword="false" /> if the type is not already declared in the compilation.</returns>
    /// <remarks>
    /// Skip if the compilation already defines this type or can access it from elsewhere.
    /// But if we have more than one match, the compiler won't be able to resolve our type references.
    /// In such a case, we'll prefer to just declare our own local symbol.
    /// </remarks>
    private bool IsTypeAlreadyFullyDeclared(string fullyQualifiedMetadataName) => this.FindTypeSymbolsIfAlreadyAvailable(fullyQualifiedMetadataName).Count == 1 || this.IsExcludedName(fullyQualifiedMetadataName);

    private ISymbol? FindTypeSymbolIfAlreadyAvailable(string fullyQualifiedMetadataName) => this.FindTypeSymbolsIfAlreadyAvailable(fullyQualifiedMetadataName).FirstOrDefault();

    private IReadOnlyList<ISymbol> FindTypeSymbolsIfAlreadyAvailable(string fullyQualifiedMetadataName)
    {
        if (this.findTypeSymbolIfAlreadyAvailableCache.TryGetValue(fullyQualifiedMetadataName, out IReadOnlyList<ISymbol>? result))
        {
            return result;
        }

        List<ISymbol>? results = null;
        if (this.compilation is object)
        {
            if (this.compilation.Assembly.GetTypeByMetadataName(fullyQualifiedMetadataName) is { } ownSymbol)
            {
                // This assembly defines it.
                // But if it defines it as a partial, we should not consider it as fully defined so we populate our side.
                if (!ownSymbol.DeclaringSyntaxReferences.Any(sr => sr.GetSyntax() is BaseTypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    results ??= new();
                    results.Add(ownSymbol);
                }
            }

            foreach (MetadataReference? reference in this.compilation.References)
            {
                if (!reference.Properties.Aliases.IsEmpty)
                {
                    // We don't (yet) generate code to leverage aliases, so we skip any symbols defined in aliased references.
                    continue;
                }

                if (this.compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referencedAssembly)
                {
                    if (referencedAssembly.GetTypeByMetadataName(fullyQualifiedMetadataName) is { } externalSymbol)
                    {
                        if (this.compilation.IsSymbolAccessibleWithin(externalSymbol, this.compilation.Assembly))
                        {
                            // A referenced assembly declares this symbol and it is accessible to our own.
                            results ??= new();
                            results.Add(externalSymbol);
                        }
                    }
                }
            }
        }

        result = (IReadOnlyList<ISymbol>?)results ?? Array.Empty<ISymbol>();
        this.findTypeSymbolIfAlreadyAvailableCache.Add(fullyQualifiedMetadataName, result);
        return result;
    }

    private ISymbol? FindExtensionMethodIfAlreadyAvailable(string fullyQualifiedTypeMetadataName, string methodName)
    {
        foreach (INamedTypeSymbol typeSymbol in this.FindTypeSymbolsIfAlreadyAvailable(fullyQualifiedTypeMetadataName).OfType<INamedTypeSymbol>())
        {
            if (typeSymbol.GetMembers(methodName) is { Length: > 0 } members)
            {
                return members[0];
            }
        }

        return null;
    }

    private MemberDeclarationSyntax? RequestInteropTypeHelper(TypeDefinitionHandle typeDefHandle, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        if (this.IsCompilerGenerated(typeDef))
        {
            return null;
        }

        // Skip if the compilation already defines this type or can access it from elsewhere.
        string name = this.Reader.GetString(typeDef.Name);
        string ns = this.Reader.GetString(typeDef.Namespace);
        bool isManagedType = this.IsManagedType(typeDefHandle);
        string fullyQualifiedName = this.GetMangledIdentifier(ns + "." + name, context.AllowMarshaling, isManagedType);

        // Skip if the compilation already defines this type or can access it from elsewhere.
        // But if we have more than one match, the compiler won't be able to resolve our type references.
        // In such a case, we'll prefer to just declare our own local symbol.
        if (this.IsTypeAlreadyFullyDeclared(fullyQualifiedName))
        {
            // The type already exists either in this project or a referenced one.
            return null;
        }

        try
        {
            StringHandle baseTypeName, baseTypeNamespace;
            this.GetBaseTypeInfo(typeDef, out baseTypeName, out baseTypeNamespace);

            MemberDeclarationSyntax? typeDeclaration;

            if ((typeDef.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
            {
                typeDeclaration = this.DeclareInterface(typeDefHandle, context);
            }
            else if (this.Reader.StringComparer.Equals(baseTypeName, nameof(ValueType)) && this.Reader.StringComparer.Equals(baseTypeNamespace, nameof(System)))
            {
                // Is this a special typedef struct?
                if (this.IsTypeDefStruct(typeDef))
                {
                    typeDeclaration = this.DeclareTypeDefStruct(typeDef, typeDefHandle);
                }
                else if (this.IsEmptyStructWithGuid(typeDef))
                {
                    typeDeclaration = this.DeclareCocreatableClass(typeDef, context);
                }
                else
                {
                    StructDeclarationSyntax structDeclaration = this.DeclareStruct(typeDefHandle, context);

                    // Proactively generate all nested types as well.
                    // If the outer struct is using ExplicitLayout, generate the nested types as unmanaged structs since that's what will be needed.
                    Context nestedContext = context;
                    bool explicitLayout = (typeDef.Attributes & TypeAttributes.ExplicitLayout) == TypeAttributes.ExplicitLayout;
                    if (context.AllowMarshaling && explicitLayout)
                    {
                        nestedContext = nestedContext with { AllowMarshaling = false };
                    }

                    foreach (TypeDefinitionHandle nestedHandle in typeDef.GetNestedTypes())
                    {
                        if (this.RequestInteropTypeHelper(nestedHandle, nestedContext) is { } nestedType)
                        {
                            structDeclaration = structDeclaration.AddMembers(nestedType);
                        }
                    }

                    typeDeclaration = structDeclaration;
                }
            }
            else if (this.Reader.StringComparer.Equals(baseTypeName, nameof(Enum)) && this.Reader.StringComparer.Equals(baseTypeNamespace, nameof(System)))
            {
                // Consider reusing .NET types like FILE_SHARE_FLAGS -> System.IO.FileShare
                typeDeclaration = this.DeclareEnum(typeDef);
            }
            else if (this.Reader.StringComparer.Equals(baseTypeName, nameof(MulticastDelegate)) && this.Reader.StringComparer.Equals(baseTypeNamespace, nameof(System)))
            {
                typeDeclaration =
                    this.IsUntypedDelegate(typeDef) ? this.DeclareUntypedDelegate(typeDef) :
                        context.AllowMarshaling ?
                            (this.useSourceGenerators ? this.DeclareTypeDefStructForNativeFunctionPointer(typeDef, context) : this.DeclareDelegate(typeDef)) :
                    this.DeclareTypeDefStructForNativeFunctionPointer(typeDef, context);
            }
            else
            {
                // not yet supported.
                return null;
            }

            // add generated code attribute.
            if (typeDeclaration is not null)
            {
                typeDeclaration = typeDeclaration
                    .WithLeadingTrivia()
                    .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute))
                    .WithLeadingTrivia(typeDeclaration.GetLeadingTrivia());
            }

            return typeDeclaration;
        }
        catch (Exception ex)
        {
            throw new GenerationFailedException($"Failed to generate {this.Reader.GetString(typeDef.Name)}{(context.AllowMarshaling ? string.Empty : " (unmanaged)")}", ex);
        }
    }

    private bool IsCompatibleWithPlatform(CustomAttributeHandleCollection customAttributesOnMember) => MetadataUtilities.IsCompatibleWithPlatform(this.Reader, this.MetadataIndex, this.compilation?.Options.Platform, customAttributesOnMember);

    private void TryGenerateTypeOrThrow(string possiblyQualifiedName)
    {
        if (!this.TryGenerateType(possiblyQualifiedName))
        {
            throw new GenerationFailedException("Unable to find expected type: " + possiblyQualifiedName);
        }
    }

    private void TryGenerateConstantOrThrow(string possiblyQualifiedName)
    {
        if (!this.TryGenerateConstant(possiblyQualifiedName, out _))
        {
            throw new GenerationFailedException("Unable to find expected constant: " + possiblyQualifiedName);
        }
    }

    private MethodDeclarationSyntax CreateAsSpanMethodOverValueAndLength(TypeSyntax spanType)
    {
        ExpressionSyntax thisValue = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("Value"));
        ExpressionSyntax thisLength = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("Length"));

        // internal X AsSpan() => this.Value is null ? default(X) : new X(this.Value, this.Length);
        return MethodDeclaration(spanType, Identifier("AsSpan"))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithExpressionBody(ArrowExpressionClause(ConditionalExpression(
                condition: IsPatternExpression(thisValue, ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression))),
                whenTrue: DefaultExpression(spanType),
                whenFalse: ObjectCreationExpression(spanType).AddArgumentListArguments(Argument(thisValue), Argument(thisLength)))))
            .WithSemicolonToken(SemicolonWithLineFeed)
            .WithLeadingTrivia(StrAsSpanComment);
    }

    private string GetNormalizedModuleName(MethodImport import)
    {
        ModuleReference module = this.Reader.GetModuleReference(import.Module);
        string moduleName = this.Reader.GetString(module.Name);
        if (CanonicalCapitalizations.TryGetValue(moduleName, out string? canonicalModuleName))
        {
            moduleName = canonicalModuleName;
        }

        return moduleName;
    }

    private string GetNamespaceForPossiblyNestedType(TypeDefinition nestedTypeDef)
    {
        if (nestedTypeDef.IsNested)
        {
            return this.GetNamespaceForPossiblyNestedType(this.Reader.GetTypeDefinition(nestedTypeDef.GetDeclaringType()));
        }
        else
        {
            return this.Reader.GetString(nestedTypeDef.Namespace);
        }
    }

    private ParameterListSyntax CreateParameterList(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature, TypeSyntaxSettings typeSettings, GeneratingElement forElement)
        => FixTrivia(ParameterList().AddParameters(methodDefinition.GetParameters().Select(this.Reader.GetParameter).Where(p => !p.Name.IsNil).Select(p => this.CreateParameter(signature, signature.ParameterTypes[p.SequenceNumber - 1], p, typeSettings, forElement)).ToArray()));

    private ParameterSyntax CreateParameter(MethodSignature<TypeHandleInfo> signature, TypeHandleInfo parameterInfo, Parameter parameter, TypeSyntaxSettings typeSettings, GeneratingElement forElement)
    {
        string name = this.Reader.GetString(parameter.Name);
        try
        {
            // TODO:
            // * Notice [Out][RAIIFree] handle producing parameters. Can we make these provide SafeHandle's?
            bool isReturnOrOutParam = parameter.SequenceNumber == 0 || (parameter.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;
            TypeSyntaxAndMarshaling parameterTypeSyntax = parameterInfo.ToTypeSyntax(typeSettings, forElement, parameter.GetCustomAttributes().QualifyWith(this), parameter.Attributes);

            // Check that CountParamIndex is valid.
            if (this.useSourceGenerators && forElement == GeneratingElement.InterfaceMember &&
                parameterTypeSyntax.NativeArrayInfo is { CountParamIndex: short countParamIndex })
            {
                // If the CountParamIndex refers to a pointer-typed parameter, we have to fall back to unmanaged for this parameter.
                // Workaround for https://github.com/dotnet/runtime/issues/120389
                if (signature.ParameterTypes[countParamIndex] is PointerTypeHandleInfo)
                {
                    typeSettings = typeSettings with { AllowMarshaling = false };
                    parameterTypeSyntax = parameterInfo.ToTypeSyntax(typeSettings, forElement, parameter.GetCustomAttributes().QualifyWith(this), parameter.Attributes);
                }
            }

            // Determine the custom attributes to apply.
            AttributeListSyntax? attributes = AttributeList();
            if (parameterTypeSyntax.Type is PointerTypeSyntax ptr || parameterTypeSyntax.ParameterModifier is null)
            {
                if ((parameter.Attributes & ParameterAttributes.Optional) == ParameterAttributes.Optional)
                {
                    attributes = attributes.AddAttributes(OptionalAttributeSyntax);
                }
            }

            SyntaxTokenList modifiers = TokenList();
            if (parameterTypeSyntax.ParameterModifier.HasValue)
            {
                modifiers = modifiers.Add(parameterTypeSyntax.ParameterModifier.Value.WithTrailingTrivia(TriviaList(Space)));
            }

            if (parameterTypeSyntax.MarshalAsAttribute is object)
            {
                if (this.useSourceGenerators)
                {
                    // Source generated com does not want [In] [Out] attributes except on array parameters.
                    if (parameterTypeSyntax.MarshalAsAttribute?.Value == UnmanagedType.LPArray)
                    {
                        if ((parameter.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out)
                        {
                            attributes = attributes.AddAttributes(OutAttributeSyntax);
                        }

                        if ((parameter.Attributes & ParameterAttributes.In) == ParameterAttributes.In)
                        {
                            attributes = attributes.AddAttributes(InAttributeSyntax);
                        }
                    }
                }
                else
                {
                    if ((parameter.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out)
                    {
                        if ((parameter.Attributes & ParameterAttributes.In) == ParameterAttributes.In)
                        {
                            attributes = attributes.AddAttributes(InAttributeSyntax);
                        }

                        if (!modifiers.Any(SyntaxKind.OutKeyword))
                        {
                            attributes = attributes.AddAttributes(OutAttributeSyntax);
                        }
                    }
                }
            }

            if (parameterTypeSyntax.MarshalUsingType is string marshalUsingType)
            {
                attributes = attributes.AddAttributes(
                    Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalUsing"))
                        .AddArgumentListArguments(AttributeArgument(TypeOfExpression(ParseName(marshalUsingType)))));
            }

            ParameterSyntax parameterSyntax = Parameter(
                attributes.Attributes.Count > 0 ? List<AttributeListSyntax>().Add(attributes) : List<AttributeListSyntax>(),
                modifiers,
                parameterTypeSyntax.Type.WithTrailingTrivia(TriviaList(Space)),
                SafeIdentifier(name),
                @default: null);
            parameterSyntax = parameterTypeSyntax.AddMarshalAs(parameterSyntax);

            if (this.FindInteropDecorativeAttribute(parameter.GetCustomAttributes(), "RetValAttribute") is not null)
            {
                parameterSyntax = parameterSyntax.WithAdditionalAnnotations(IsRetValAnnotation);
            }

            return parameterSyntax;
        }
        catch (Exception ex)
        {
            throw new GenerationFailedException("Failed while generating parameter: " + name, ex);
        }
    }

    private void DeclareSliceAtNullExtensionMethodIfNecessary()
    {
        if (this.sliceAtNullMethodDecl is null)
        {
            IdentifierNameSyntax valueParam = IdentifierName("value");
            IdentifierNameSyntax lengthLocal = IdentifierName("length");
            TypeSyntax charSpan = MakeReadOnlySpanOfT(PredefinedType(Token(SyntaxKind.CharKeyword)));

            // int length = value.IndexOf('\0');
            StatementSyntax lengthLocalDeclaration =
                LocalDeclarationStatement(VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword))).AddVariables(
                    VariableDeclarator(lengthLocal.Identifier).WithInitializer(EqualsValueClause(
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(MemoryExtensions.IndexOf))),
                            ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))))))));

            // static ReadOnlySpan<char> SliceAtNull(this ReadOnlySpan<char> value)
            this.sliceAtNullMethodDecl = MethodDeclaration(charSpan, SliceAtNullMethodName.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(Parameter(valueParam.Identifier).WithType(charSpan).AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword)))
                .WithBody(Block().AddStatements(
                    lengthLocalDeclaration,
                    //// return length < 0 ? value : value.Slice(0, length);
                    ReturnStatement(ConditionalExpression(
                        BinaryExpression(SyntaxKind.LessThanExpression, lengthLocal, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                        valueParam,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<char>.Slice))),
                            ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))), Argument(lengthLocal)))))));
        }

        this.volatileCode.AddInlineArrayIndexerExtension(this.sliceAtNullMethodDecl);
    }

    private IEnumerable<NamespaceMetadata> GetNamespacesToSearch(string? @namespace)
    {
        if (@namespace is object)
        {
            return this.MetadataIndex.MetadataByNamespace.TryGetValue(@namespace, out NamespaceMetadata? metadata)
                ? new[] { metadata }
                : Array.Empty<NamespaceMetadata>();
        }
        else
        {
            return this.MetadataIndex.MetadataByNamespace.Values;
        }
    }

    [DebuggerDisplay($"AllowMarshaling: {{{nameof(AllowMarshaling)}}}")]
    internal record struct Context
    {
        /// <summary>
        /// Gets a value indicating whether the context permits marshaling.
        /// This may be more constrained than <see cref="GeneratorOptions.AllowMarshaling"/> when within the context of a union struct.
        /// </summary>
        internal bool AllowMarshaling { get; init; }

        internal TypeSyntaxSettings Filter(TypeSyntaxSettings settings)
        {
            if (!this.AllowMarshaling && settings.AllowMarshaling)
            {
                settings = settings with { AllowMarshaling = false };
            }

            return settings;
        }
    }

    internal struct NativeArrayInfo
    {
        internal short? CountParamIndex { get; init; }

        internal int? CountConst { get; init; }
    }

    internal struct MemorySize
    {
        internal short? BytesParamIndex { get; init; }
    }

    private class DirectiveTriviaRemover : CSharpSyntaxRewriter
    {
        internal static readonly DirectiveTriviaRemover Instance = new();

        private DirectiveTriviaRemover()
        {
        }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            trivia.IsKind(SyntaxKind.IfDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.DisabledTextTrivia)
            ? default : trivia;
    }
}
