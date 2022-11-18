// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Windows.SDK.Win32Docs;
using static System.FormattableString;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;
using static Microsoft.Windows.CsWin32.SimpleSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// The core of the source generator.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplayString) + ",nq}")]
public partial class Generator : IDisposable
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

    private readonly Dictionary<string, ISymbol?> findTypeSymbolIfAlreadyAvailableCache = new(StringComparer.Ordinal);
    private readonly Rental<MetadataReader> metadataReader;
    private readonly GeneratorOptions options;
    private readonly CSharpCompilation? compilation;
    private readonly CSharpParseOptions? parseOptions;
    private readonly bool canUseSpan;
    private readonly bool canCallCreateSpan;
    private readonly bool canUseUnsafeAsRef;
    private readonly bool canUseUnsafeNullRef;
    private readonly bool unscopedRefAttributePredefined;
    private readonly bool comIIDInterfacePredefined;
    private readonly bool getDelegateForFunctionPointerGenericExists;
    private readonly bool generateSupportedOSPlatformAttributes;
    private readonly bool generateSupportedOSPlatformAttributesOnInterfaces; // only supported on net6.0 (https://github.com/dotnet/runtime/pull/48838)
    private readonly bool generateDefaultDllImportSearchPathsAttribute;
    private readonly GeneratedCode committedCode = new();
    private readonly GeneratedCode volatileCode;
    private readonly IdentifierNameSyntax methodsAndConstantsClassName;
    private readonly HashSet<string> injectedPInvokeHelperMethods = new();
    private readonly HashSet<string> injectedPInvokeHelperMethodsToFriendlyOverloadsExtensions = new();
    private readonly HashSet<string> injectedPInvokeMacros = new();
    private readonly Dictionary<TypeDefinitionHandle, bool> managedTypesCheck = new();
    private bool needsWinRTCustomMarshaler;
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

        PInvokeMacros = ((ClassDeclarationSyntax)member).Members.OfType<MethodDeclarationSyntax>().ToDictionary(m => m.Identifier.ValueText, m => m);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Generator"/> class.
    /// </summary>
    /// <param name="metadataLibraryPath">The path to the winmd metadata to generate APIs from.</param>
    /// <param name="docs">The API docs to include in the generated code.</param>
    /// <param name="options">Options that influence the result of generation.</param>
    /// <param name="compilation">The compilation that the generated code will be added to.</param>
    /// <param name="parseOptions">The parse options that will be used for the generated code.</param>
    public Generator(string metadataLibraryPath, Docs? docs, GeneratorOptions options, CSharpCompilation? compilation = null, CSharpParseOptions? parseOptions = null)
    {
        this.InputAssemblyName = Path.GetFileNameWithoutExtension(metadataLibraryPath);
        this.MetadataIndex = MetadataIndex.Get(metadataLibraryPath, compilation?.Options.Platform);
        this.ApiDocs = docs;
        this.metadataReader = MetadataIndex.GetMetadataReader(metadataLibraryPath);

        this.options = options;
        this.options.Validate();
        this.compilation = compilation;
        this.parseOptions = parseOptions;
        this.volatileCode = new(this.committedCode);

        this.canUseSpan = this.compilation?.GetTypeByMetadataName(typeof(Span<>).FullName) is not null;
        this.canCallCreateSpan = this.compilation?.GetTypeByMetadataName(typeof(MemoryMarshal).FullName)?.GetMembers("CreateSpan").Any() is true;
        this.canUseUnsafeAsRef = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("AsRef").Any() is true;
        this.canUseUnsafeNullRef = this.compilation?.GetTypeByMetadataName(typeof(Unsafe).FullName)?.GetMembers("NullRef").Any() is true;
        this.unscopedRefAttributePredefined = this.FindTypeSymbolIfAlreadyAvailable("System.Diagnostics.CodeAnalysis.UnscopedRefAttribute") is not null;
        this.comIIDInterfacePredefined = this.FindTypeSymbolIfAlreadyAvailable($"{this.Namespace}.{IComIIDGuidInterfaceName}") is not null;
        this.getDelegateForFunctionPointerGenericExists = this.compilation?.GetTypeByMetadataName(typeof(Marshal).FullName)?.GetMembers(nameof(Marshal.GetDelegateForFunctionPointer)).Any(m => m is IMethodSymbol { IsGenericMethod: true }) is true;
        this.generateDefaultDllImportSearchPathsAttribute = this.compilation?.GetTypeByMetadataName(typeof(DefaultDllImportSearchPathsAttribute).FullName) is object;
        if (this.FindTypeSymbolIfAlreadyAvailable("System.Runtime.Versioning.SupportedOSPlatformAttribute") is { } attribute)
        {
            this.generateSupportedOSPlatformAttributes = true;
            AttributeData usageAttribute = attribute.GetAttributes().Single(att => att.AttributeClass?.Name == nameof(AttributeUsageAttribute));
            var targets = (AttributeTargets)usageAttribute.ConstructorArguments[0].Value!;
            this.generateSupportedOSPlatformAttributesOnInterfaces = (targets & AttributeTargets.Interface) == AttributeTargets.Interface;
        }

        // Convert some of our CanUse fields to preprocessor symbols so our templates can use them.
        if (this.parseOptions is not null)
        {
            List<string> extraSymbols = new();
            AddSymbolIf(this.canUseSpan, "canUseSpan");
            AddSymbolIf(this.canCallCreateSpan, "canCallCreateSpan");
            AddSymbolIf(this.canUseUnsafeAsRef, "canUseUnsafeAsRef");
            AddSymbolIf(this.canUseUnsafeNullRef, "canUseUnsafeNullRef");

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
        this.comSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true };
        this.extensionMethodSignatureTypeSettings = this.generalTypeSettings with { QualifyNames = true };
        this.functionPointerTypeSettings = this.generalTypeSettings with { QualifyNames = true };
        this.errorMessageTypeSettings = this.generalTypeSettings with { QualifyNames = true, Generator = null }; // Avoid risk of infinite recursion from errors in ToTypeSyntax

        this.methodsAndConstantsClassName = IdentifierName(options.ClassName);
    }

    private enum FriendlyOverloadOf
    {
        ExternMethod,
        StructMethod,
        InterfaceMethod,
    }

    private enum Feature
    {
        InterfaceStaticMembers,
    }

    internal ImmutableDictionary<string, string> BannedAPIs => GetBannedAPIs(this.options);

    internal SuperGenerator? SuperGenerator { get; set; }

    internal GeneratorOptions Options => this.options;

    internal string InputAssemblyName { get; }

    internal MetadataIndex MetadataIndex { get; }

    internal Docs? ApiDocs { get; }

    internal MetadataReader Reader => this.metadataReader.Value;

    internal LanguageVersion LanguageVersion => this.parseOptions?.LanguageVersion ?? LanguageVersion.CSharp9;

    /// <summary>
    /// Gets the default generation context to use.
    /// </summary>
    internal Context DefaultContext => new() { AllowMarshaling = this.options.AllowMarshaling };

    private bool WideCharOnly => this.options.WideCharOnly;

    private string Namespace => this.InputAssemblyName;

    private SyntaxKind Visibility => this.options.Public ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword;

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

    /// <summary>
    /// Generates all extern methods, structs, delegates, constants as defined by the source metadata.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public void GenerateAll(CancellationToken cancellationToken)
    {
        this.GenerateAllExternMethods(cancellationToken);

        // Also generate all structs/enum types too, even if not referenced by a method,
        // since some methods use `void*` types and require structs at runtime.
        this.RequestAllInteropTypes(cancellationToken);

        this.GenerateAllConstants(cancellationToken);

        this.GenerateAllMacros(cancellationToken);
    }

    /// <inheritdoc cref="TryGenerate(string, out IReadOnlyList{string}, CancellationToken)"/>
    public bool TryGenerate(string apiNameOrModuleWildcard, CancellationToken cancellationToken) => this.TryGenerate(apiNameOrModuleWildcard, out _, cancellationToken);

    /// <summary>
    /// Generates code for a given API.
    /// </summary>
    /// <param name="apiNameOrModuleWildcard">The name of the method, struct or constant. Or the name of a module with a ".*" suffix in order to generate all methods and supporting types for the specified module.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="apiNameOrModuleWildcard"/> matched on.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> if any matching APIs were found and generated; <see langword="false"/> otherwise.</returns>
    public bool TryGenerate(string apiNameOrModuleWildcard, out IReadOnlyList<string> preciseApi, CancellationToken cancellationToken)
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
    public bool TryGenerateNamespace(string @namespace, out IReadOnlyList<string> preciseApi)
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

    /// <inheritdoc cref="MetadataIndex.TryGetEnumName(MetadataReader, string, out string?)"/>
    public bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum) => this.MetadataIndex.TryGetEnumName(this.Reader, enumValueName, out declaringEnum);

    /// <summary>
    /// Generates a projection of all extern methods and their supporting types.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public void GenerateAllExternMethods(CancellationToken cancellationToken)
    {
        foreach (MethodDefinitionHandle methodHandle in this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetMethods()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodHandle);
            if (this.IsCompatibleWithPlatform(methodDef.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestExternMethod(methodHandle);
                    });
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this method is not available for this platform, so skip this method.
                }
            }
        }
    }

    /// <summary>
    /// Generates a projection of all constants.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public void GenerateAllConstants(CancellationToken cancellationToken)
    {
        foreach (FieldDefinitionHandle fieldDefHandle in this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetFields()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            if (this.IsCompatibleWithPlatform(fieldDef.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestConstant(fieldDefHandle);
                    });
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this field is not available for this platform, so skip this method.
                }
            }
        }
    }

    /// <summary>
    /// Generates a projection of all macros.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public void GenerateAllMacros(CancellationToken cancellationToken)
    {
        foreach (KeyValuePair<string, MethodDeclarationSyntax> macro in PInvokeMacros)
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

    /// <summary>
    /// Generates all extern methods exported from a particular module, along with all their supporting types.
    /// </summary>
    /// <param name="moduleName">The name of the module for whose exports extern methods should be generated for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if a matching module name was found and extern methods generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateAllExternMethods(string moduleName, CancellationToken cancellationToken)
    {
        bool successful = false;
        foreach (MethodDefinitionHandle methodHandle in this.MetadataIndex.Apis.SelectMany(api => this.Reader.GetTypeDefinition(api).GetMethods()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodHandle);
            ModuleReferenceHandle moduleHandle = methodDef.GetImport().Module;
            if (moduleHandle.IsNil)
            {
                continue;
            }

            ModuleReference module = this.Reader.GetModuleReference(moduleHandle);
            if (this.Reader.StringComparer.Equals(module.Name, moduleName + ".dll", ignoreCase: true))
            {
                string? bannedReason = null;
                foreach (KeyValuePair<string, string> bannedApi in this.BannedAPIs)
                {
                    if (this.Reader.StringComparer.Equals(methodDef.Name, bannedApi.Key))
                    {
                        // Skip a banned API.
                        bannedReason = bannedApi.Value;
                        continue;
                    }
                }

                if (bannedReason is object)
                {
                    continue;
                }

                if (this.IsCompatibleWithPlatform(methodDef.GetCustomAttributes()))
                {
                    try
                    {
                        this.volatileCode.GenerationTransaction(delegate
                        {
                            this.RequestExternMethod(methodHandle);
                        });
                    }
                    catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                    {
                        // Something transitively required for this method is not available for this platform, so skip this method.
                    }
                }

                successful = true;
            }
        }

        return successful;
    }

    /// <summary>
    /// Generate code for the named extern method, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the extern method, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the extern method generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateExternMethod(string possiblyQualifiedName, out IReadOnlyList<string> preciseApi)
    {
        if (possiblyQualifiedName is null)
        {
            throw new ArgumentNullException(nameof(possiblyQualifiedName));
        }

        if (this.GetMethodByName(possiblyQualifiedName) is MethodDefinitionHandle methodDefHandle)
        {
            MethodDefinition methodDef = this.Reader.GetMethodDefinition(methodDefHandle);
            string methodName = this.Reader.StringComparer.Equals(methodDef.Name, possiblyQualifiedName) ? possiblyQualifiedName : this.Reader.GetString(methodDef.Name);
            if (this.BannedAPIs.TryGetValue(methodName, out string? reason))
            {
                throw new NotSupportedException(reason);
            }

            this.volatileCode.GenerationTransaction(delegate
            {
                this.RequestExternMethod(methodDefHandle);
            });

            string methodNamespace = this.Reader.GetString(this.Reader.GetTypeDefinition(methodDef.GetDeclaringType()).Namespace);
            preciseApi = ImmutableList.Create($"{methodNamespace}.{methodName}");
            return true;
        }

        preciseApi = ImmutableList<string>.Empty;
        return false;
    }

    /// <inheritdoc cref="TryGenerateType(string, out IReadOnlyList{string})"/>
    public bool TryGenerateType(string possiblyQualifiedName) => this.TryGenerateType(possiblyQualifiedName, out _);

    /// <summary>
    /// Generate code for the named type, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the interop type, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the type generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateType(string possiblyQualifiedName, out IReadOnlyList<string> preciseApi)
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

        if (SpecialTypeDefNames.Contains(typeName))
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
    /// Generates code for all constants with a common prefix.
    /// </summary>
    /// <param name="constantNameWithTrailingWildcard">The prefix, including a trailing <c>*</c>. A qualifying namespace is allowed.</param>
    /// <returns><see langword="true" /> if at least one constant matched the prefix and was generated; otherwise <see langword="false" />.</returns>
    public bool TryGenerateConstants(string constantNameWithTrailingWildcard)
    {
        if (constantNameWithTrailingWildcard is null)
        {
            throw new ArgumentNullException(nameof(constantNameWithTrailingWildcard));
        }

        if (constantNameWithTrailingWildcard.Length < 2 || constantNameWithTrailingWildcard[constantNameWithTrailingWildcard.Length - 1] != '*')
        {
            throw new ArgumentException("A name with a wildcard ending is expected.", nameof(constantNameWithTrailingWildcard));
        }

        TrySplitPossiblyQualifiedName(constantNameWithTrailingWildcard, out string? constantNamespace, out string constantName);
        string prefix = constantName.Substring(0, constantName.Length - 1);
        IEnumerable<NamespaceMetadata>? namespaces = this.GetNamespacesToSearch(constantNamespace);
        IEnumerable<FieldDefinitionHandle>? matchingFieldHandles = from ns in namespaces
                                                                   from field in ns.Fields
                                                                   where field.Key.StartsWith(prefix, StringComparison.Ordinal)
                                                                   select field.Value;

        bool anyMatch = false;
        foreach (FieldDefinitionHandle fieldHandle in matchingFieldHandles)
        {
            FieldDefinition field = this.Reader.GetFieldDefinition(fieldHandle);
            if (this.IsCompatibleWithPlatform(field.GetCustomAttributes()))
            {
                try
                {
                    this.volatileCode.GenerationTransaction(delegate
                    {
                        this.RequestConstant(fieldHandle);
                    });
                    anyMatch = true;
                }
                catch (GenerationFailedException ex) when (IsPlatformCompatibleException(ex))
                {
                    // Something transitively required for this API is not available for this platform, so skip this method.
                }
            }
        }

        return anyMatch;
    }

    /// <summary>
    /// Generate code for the named constant, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the constant, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the constant generated; otherwise <see langword="false"/>.</returns>
    public bool TryGenerateConstant(string possiblyQualifiedName, out IReadOnlyList<string> preciseApi)
    {
        if (possiblyQualifiedName is null)
        {
            throw new ArgumentNullException(nameof(possiblyQualifiedName));
        }

        TrySplitPossiblyQualifiedName(possiblyQualifiedName, out string? constantNamespace, out string constantName);
        var matchingFieldHandles = new List<FieldDefinitionHandle>();
        IEnumerable<NamespaceMetadata>? namespaces = this.GetNamespacesToSearch(constantNamespace);

        foreach (NamespaceMetadata? nsMetadata in namespaces)
        {
            if (nsMetadata.Fields.TryGetValue(constantName, out FieldDefinitionHandle fieldDefHandle))
            {
                matchingFieldHandles.Add(fieldDefHandle);
            }
        }

        if (matchingFieldHandles.Count == 1)
        {
            this.volatileCode.GenerationTransaction(delegate
            {
                this.RequestConstant(matchingFieldHandles[0]);
            });

            FieldDefinition fd = this.Reader.GetFieldDefinition(matchingFieldHandles[0]);
            TypeDefinition td = this.Reader.GetTypeDefinition(fd.GetDeclaringType());
            preciseApi = ImmutableList.Create($"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(fd.Name)}");
            return true;
        }
        else if (matchingFieldHandles.Count > 1)
        {
            preciseApi = ImmutableList.CreateRange(
                matchingFieldHandles.Select(h =>
                {
                    FieldDefinition fd = this.Reader.GetFieldDefinition(h);
                    TypeDefinition td = this.Reader.GetTypeDefinition(fd.GetDeclaringType());
                    return $"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(fd.Name)}";
                }));
            return false;
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
    public bool TryGenerateMacro(string macroName, out IReadOnlyList<string> preciseApi)
    {
        if (macroName is null)
        {
            throw new ArgumentNullException(nameof(macroName));
        }

        if (!PInvokeMacros.TryGetValue(macroName, out MethodDeclarationSyntax macro))
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

    /// <summary>
    /// Produces a sequence of suggested APIs with a similar name to the specified one.
    /// </summary>
    /// <param name="name">The user-supplied name.</param>
    /// <returns>A sequence of API names.</returns>
    public IEnumerable<string> GetSuggestions(string name)
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
        foreach (NamespaceMetadata nsMetadata in this.MetadataIndex.MetadataByNamespace.Values)
        {
            foreach (string candidate in nsMetadata.Fields.Keys.Concat(nsMetadata.Types.Keys).Concat(nsMetadata.Methods.Keys))
            {
                if (candidate.Contains(name))
                {
                    yield return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Collects the result of code generation.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All the generated source files, keyed by filename.</returns>
    public IReadOnlyDictionary<string, CompilationUnitSyntax> GetCompilationUnits(CancellationToken cancellationToken)
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

        if (this.options.EmitSingleFile)
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
            if (this.options.EmitSingleFile)
            {
                KeyValuePair<string, CompilationUnitSyntax> originalEntry = normalizedResults.Single();
                normalizedResults[originalEntry.Key] = originalEntry.Value.WithLeadingTrivia().AddAttributeLists(CsWin32StampAttribute).WithLeadingTrivia(originalEntry.Value.GetLeadingTrivia());
            }
            else
            {
                normalizedResults.Add(string.Format(CultureInfo.InvariantCulture, FilenamePattern, "CsWin32Stamp"), CompilationUnit().AddAttributeLists(CsWin32StampAttribute).WithLeadingTrivia(FileHeader));
            }
        }

        if (this.needsWinRTCustomMarshaler)
        {
            string? marshalerText = FetchTemplateText(WinRTCustomMarshalerClass);
            if (marshalerText == null)
            {
                throw new GenerationFailedException($"Failed to get template for \"{WinRTCustomMarshalerClass}\".");
            }

            SyntaxTree? marshalerContents = SyntaxFactory.ParseSyntaxTree(marshalerText);
            if (marshalerContents == null)
            {
                throw new GenerationFailedException($"Failed adding \"{WinRTCustomMarshalerClass}\".");
            }

            CompilationUnitSyntax? compilationUnit = ((CompilationUnitSyntax)marshalerContents.GetRoot())
                .WithLeadingTrivia(ParseLeadingTrivia(AutoGeneratedHeader));

            normalizedResults.Add(
                string.Format(CultureInfo.InvariantCulture, FilenamePattern, WinRTCustomMarshalerClass),
                compilationUnit);
        }

        return normalizedResults;
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

    internal static bool IsUntypedDelegate(MetadataReader reader, TypeDefinition typeDef) => reader.StringComparer.Equals(typeDef.Name, "PROC") || reader.StringComparer.Equals(typeDef.Name, "FARPROC");

    internal static string ReplaceCommonNamespaceWithAlias(Generator? generator, string fullNamespace)
    {
        return generator is object && generator.TryStripCommonNamespace(fullNamespace, out string? stripped) ? (stripped.Length > 0 ? $"{GlobalWinmdRootNamespaceAlias}.{stripped}" : GlobalWinmdRootNamespaceAlias) : $"global::{fullNamespace}";
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

    internal bool IsAttribute(CustomAttribute attribute, string ns, string name) => MetadataUtilities.IsAttribute(this.Reader, attribute, ns, name);

    internal bool TryGetHandleReleaseMethod(EntityHandle handleStructDefHandle, [NotNullWhen(true)] out string? releaseMethod)
    {
        if (handleStructDefHandle.IsNil)
        {
            releaseMethod = null;
            return false;
        }

        if (handleStructDefHandle.Kind == HandleKind.TypeReference)
        {
            if (this.TryGetTypeDefHandle((TypeReferenceHandle)handleStructDefHandle, out TypeDefinitionHandle typeDefHandle))
            {
                return this.TryGetHandleReleaseMethod(typeDefHandle, out releaseMethod);
            }
        }
        else if (handleStructDefHandle.Kind == HandleKind.TypeDefinition)
        {
            return this.TryGetHandleReleaseMethod((TypeDefinitionHandle)handleStructDefHandle, out releaseMethod);
        }

        releaseMethod = null;
        return false;
    }

    internal bool TryGetHandleReleaseMethod(TypeDefinitionHandle handleStructDefHandle, [NotNullWhen(true)] out string? releaseMethod)
    {
        return this.MetadataIndex.HandleTypeReleaseMethod.TryGetValue(handleStructDefHandle, out releaseMethod);
    }

    internal void RequestAllInteropTypes(CancellationToken cancellationToken)
    {
        foreach (TypeDefinitionHandle typeDefinitionHandle in this.Reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefinitionHandle);
            if (typeDef.BaseType.IsNil && (typeDef.Attributes & TypeAttributes.Interface) != TypeAttributes.Interface)
            {
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

    internal void RequestExternMethod(MethodDefinitionHandle methodDefinitionHandle)
    {
        if (methodDefinitionHandle.IsNil)
        {
            return;
        }

        MethodDefinition methodDefinition = this.Reader.GetMethodDefinition(methodDefinitionHandle);
        if (!this.IsCompatibleWithPlatform(methodDefinition.GetCustomAttributes()))
        {
            // We've been asked for an interop type that does not apply. This happens because the metadata
            // may use a TypeReferenceHandle or TypeDefinitionHandle to just one of many arch-specific definitions of this type.
            // Try to find the appropriate definition for our target architecture.
            TypeDefinition declaringTypeDef = this.Reader.GetTypeDefinition(methodDefinition.GetDeclaringType());
            string ns = this.Reader.GetString(declaringTypeDef.Namespace);
            string methodName = this.Reader.GetString(methodDefinition.Name);
            if (this.MetadataIndex.MetadataByNamespace[ns].MethodsForOtherPlatform.Contains(methodName))
            {
                throw new PlatformIncompatibleException($"Request for method ({methodName}) that is not available given the target platform.");
            }
        }

        this.volatileCode.GenerateMethod(methodDefinitionHandle, () => this.DeclareExternMethod(methodDefinitionHandle));
    }

    internal bool IsInterface(HandleTypeHandleInfo typeInfo)
    {
        TypeDefinitionHandle tdh = default;
        if (typeInfo.Handle.Kind == HandleKind.TypeReference)
        {
            var trh = (TypeReferenceHandle)typeInfo.Handle;
            this.TryGetTypeDefHandle(trh, out tdh);
        }
        else if (typeInfo.Handle.Kind == HandleKind.TypeDefinition)
        {
            tdh = (TypeDefinitionHandle)typeInfo.Handle;
        }

        return !tdh.IsNil && (this.Reader.GetTypeDefinition(tdh).Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
    }

    internal bool IsInterface(TypeHandleInfo handleInfo)
    {
        if (handleInfo is HandleTypeHandleInfo typeInfo)
        {
            return this.IsInterface(typeInfo);
        }
        else if (handleInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo typeInfo2 })
        {
            return this.IsInterface(typeInfo2);
        }

        return false;
    }

    internal bool IsInterface(TypeReferenceHandle typeRefHandle)
    {
        if (this.TryGetTypeDefHandle(typeRefHandle, out TypeDefinitionHandle typeDefHandle))
        {
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
            return (typeDef.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface;
        }

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

        bool hasUnmanagedName = this.HasUnmanagedSuffix(context.AllowMarshaling, this.IsManagedType(typeDefHandle));
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

                this.needsWinRTCustomMarshaler |= typeDeclaration.DescendantNodes().OfType<AttributeSyntax>()
                    .Any(a => a.Name.ToString() == "MarshalAs" && a.ToString().Contains(WinRTCustomMarshalerFullName));

                this.volatileCode.AddInteropType(typeDefHandle, hasUnmanagedName, typeDeclaration);
            }
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
                        throw new GenerationFailedException($"Input metadata file \"{scope}\" has not been provided.");
                    }
                }
            }
        }
    }

    internal void RequestConstant(FieldDefinitionHandle fieldDefHandle)
    {
        this.volatileCode.GenerateConstant(fieldDefHandle, delegate
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            FieldDeclarationSyntax constantDeclaration = this.DeclareConstant(fieldDef);

            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature<TypeHandleInfo, SignatureHandleProvider.IGenericContext?>(SignatureHandleProvider.Instance, null) with { IsConstantField = true };
            TypeDefinitionHandle? fieldType = null;
            if (fieldTypeInfo is HandleTypeHandleInfo handleInfo && this.IsTypeDefStruct(handleInfo) && handleInfo.Handle.Kind == HandleKind.TypeReference)
            {
                TypeReference tr = this.Reader.GetTypeReference((TypeReferenceHandle)handleInfo.Handle);
                string fieldTypeName = this.Reader.GetString(tr.Name);
                if (!TypeDefsThatDoNotNestTheirConstants.Contains(fieldTypeName) && this.TryGetTypeDefHandle(tr, out TypeDefinitionHandle candidate))
                {
                    fieldType = candidate;
                }
            }

            this.volatileCode.AddConstant(fieldDefHandle, constantDeclaration, fieldType);
        });
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
        });
    }

    internal TypeSyntax? RequestSafeHandle(string releaseMethod)
    {
        if (!this.options.UseSafeHandles)
        {
            return null;
        }

        try
        {
            if (this.volatileCode.TryGetSafeHandleForReleaseMethod(releaseMethod, out TypeSyntax? safeHandleType))
            {
                return safeHandleType;
            }

            if (BclInteropSafeHandles.TryGetValue(releaseMethod, out TypeSyntax? bclType))
            {
                return bclType;
            }

            string safeHandleClassName = $"{releaseMethod}SafeHandle";

            MethodDefinitionHandle? releaseMethodHandle = this.GetMethodByName(releaseMethod);
            if (!releaseMethodHandle.HasValue)
            {
                throw new GenerationFailedException("Unable to find release method named: " + releaseMethod);
            }

            MethodDefinition releaseMethodDef = this.Reader.GetMethodDefinition(releaseMethodHandle.Value);
            string releaseMethodModule = this.GetNormalizedModuleName(releaseMethodDef.GetImport());

            IdentifierNameSyntax? safeHandleTypeIdentifier = IdentifierName(safeHandleClassName);
            safeHandleType = safeHandleTypeIdentifier;

            MethodSignature<TypeHandleInfo> releaseMethodSignature = releaseMethodDef.DecodeSignature(SignatureHandleProvider.Instance, null);
            TypeHandleInfo releaseMethodParameterTypeHandleInfo = releaseMethodSignature.ParameterTypes[0];
            TypeSyntaxAndMarshaling releaseMethodParameterType = releaseMethodParameterTypeHandleInfo.ToTypeSyntax(this.externSignatureTypeSettings, default);

            // If the release method takes more than one parameter, we can't generate a SafeHandle for it.
            if (releaseMethodSignature.RequiredParameterCount != 1)
            {
                safeHandleType = null;
            }

            // If the handle type is *always* 64-bits, even in 32-bit processes, SafeHandle cannot represent it, since it's based on IntPtr.
            // We could theoretically do this for x64-specific compilations though if required.
            if (!this.TryGetTypeDefFieldType(releaseMethodParameterTypeHandleInfo, out TypeHandleInfo? typeDefStructFieldType))
            {
                safeHandleType = null;
            }

            if (!this.IsSafeHandleCompatibleTypeDefFieldType(typeDefStructFieldType))
            {
                safeHandleType = null;
            }

            this.volatileCode.AddSafeHandleNameForReleaseMethod(releaseMethod, safeHandleType);

            if (safeHandleType is null)
            {
                return safeHandleType;
            }

            if (this.FindTypeSymbolIfAlreadyAvailable($"{this.Namespace}.{safeHandleType}") is object)
            {
                return safeHandleType;
            }

            this.RequestExternMethod(releaseMethodHandle.Value);

            // Collect all the known invalid values for this handle.
            // If no invalid values are given (e.g. BSTR), we'll just assume 0 is invalid.
            HashSet<IntPtr> invalidHandleValues = this.GetInvalidHandleValues(((HandleTypeHandleInfo)releaseMethodParameterTypeHandleInfo).Handle);
            IntPtr preferredInvalidValue = GetPreferredInvalidHandleValue(invalidHandleValues);

            CustomAttributeHandleCollection? atts = this.GetReturnTypeCustomAttributes(releaseMethodDef);
            TypeSyntaxAndMarshaling releaseMethodReturnType = releaseMethodSignature.ReturnType.ToTypeSyntax(this.externSignatureTypeSettings, atts);

            this.TryGetRenamedMethod(releaseMethod, out string? renamedReleaseMethod);

            var members = new List<MemberDeclarationSyntax>();

            MemberAccessExpressionSyntax thisHandle = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("handle"));
            ExpressionSyntax intptrZero = DefaultExpression(IntPtrTypeSyntax);
            ExpressionSyntax invalidHandleIntPtr = IntPtrExpr(preferredInvalidValue);

            // private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
            IdentifierNameSyntax invalidValueFieldName = IdentifierName("INVALID_HANDLE_VALUE");
            members.Add(FieldDeclaration(VariableDeclaration(IntPtrTypeSyntax).AddVariables(
                VariableDeclarator(invalidValueFieldName.Identifier).WithInitializer(EqualsValueClause(invalidHandleIntPtr))))
                .AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword)));

            // public SafeHandle() : base(INVALID_HANDLE_VALUE, true)
            members.Add(ConstructorDeclaration(safeHandleTypeIdentifier.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithInitializer(ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, ArgumentList().AddArguments(
                    Argument(invalidValueFieldName),
                    Argument(LiteralExpression(SyntaxKind.TrueLiteralExpression)))))
                .WithBody(Block()));

            // public SafeHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(INVALID_HANDLE_VALUE, ownsHandle) { this.SetHandle(preexistingHandle); }
            const string preexistingHandleName = "preexistingHandle";
            const string ownsHandleName = "ownsHandle";
            members.Add(ConstructorDeclaration(safeHandleTypeIdentifier.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility))
                .AddParameterListParameters(
                    Parameter(Identifier(preexistingHandleName)).WithType(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))),
                    Parameter(Identifier(ownsHandleName)).WithType(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)))
                        .WithDefault(EqualsValueClause(LiteralExpression(SyntaxKind.TrueLiteralExpression))))
                .WithInitializer(ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, ArgumentList().AddArguments(
                    Argument(invalidValueFieldName),
                    Argument(IdentifierName(ownsHandleName)))))
                .WithBody(Block().AddStatements(
                    ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("SetHandle")))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName(preexistingHandleName)))))))));

            // public override bool IsInvalid => this.handle.ToInt64() == 0 || this.handle.ToInt64() == -1;
            ExpressionSyntax thisHandleToInt64 = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, thisHandle, IdentifierName(nameof(IntPtr.ToInt64))), ArgumentList());
            ExpressionSyntax overallTest = invalidHandleValues.Count == 0
                ? LiteralExpression(SyntaxKind.FalseLiteralExpression)
                : CompoundExpression(SyntaxKind.LogicalOrExpression, invalidHandleValues.Select(v => BinaryExpression(SyntaxKind.EqualsExpression, thisHandleToInt64, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(v.ToInt64())))));
            members.Add(PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), nameof(SafeHandle.IsInvalid))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
                .WithExpressionBody(ArrowExpressionClause(overallTest))
                .WithSemicolonToken(SemicolonWithLineFeed));

            // (struct)this.handle or (struct)checked((fieldType)(nint))this.handle, as appropriate.
            bool implicitConversion = typeDefStructFieldType is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr } or PointerTypeHandleInfo;
            ArgumentSyntax releaseHandleArgument = Argument(CastExpression(
                releaseMethodParameterType.Type,
                implicitConversion ? thisHandle : CheckedExpression(CastExpression(typeDefStructFieldType!.ToTypeSyntax(this.fieldTypeSettings, null).Type, CastExpression(IdentifierName("nint"), thisHandle)))));

            // protected override bool ReleaseHandle() => ReleaseMethod((struct)this.handle);
            // Special case release functions based on their return type as follows: (https://github.com/microsoft/win32metadata/issues/25)
            //  * bool => true is success
            //  * int => zero is success
            //  * uint => zero is success
            //  * byte => non-zero is success
            ExpressionSyntax releaseInvocation = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(this.options.ClassName),
                    IdentifierName(renamedReleaseMethod ?? releaseMethod)),
                ArgumentList().AddArguments(releaseHandleArgument));
            BlockSyntax? releaseBlock = null;
            if (!(releaseMethodReturnType.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.BoolKeyword } } ||
                releaseMethodReturnType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "BOOL" } } }))
            {
                switch (releaseMethodReturnType.Type)
                {
                    case PredefinedTypeSyntax predefined:
                        SyntaxKind returnType = predefined.Keyword.Kind();
                        if (returnType == SyntaxKind.IntKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.UIntKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.ByteKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.NotEqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.VoidKeyword)
                        {
                            releaseBlock = Block(
                                ExpressionStatement(releaseInvocation),
                                ReturnStatement(LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                        }
                        else
                        {
                            throw new NotSupportedException($"Return type {returnType} on release method {releaseMethod} not supported.");
                        }

                        break;
                    case QualifiedNameSyntax { Right: IdentifierNameSyntax identifierName }:
                        switch (identifierName.Identifier.ValueText)
                        {
                            case "NTSTATUS":
                                this.TryGenerateConstantOrThrow("STATUS_SUCCESS");
                                ExpressionSyntax statusSuccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.NTSTATUS"), IdentifierName("STATUS_SUCCESS"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, statusSuccess);
                                break;
                            case "HRESULT":
                                this.TryGenerateConstantOrThrow("S_OK");
                                ExpressionSyntax ok = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.HRESULT"), IdentifierName("S_OK"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, ok);
                                break;
                            case "WIN32_ERROR":
                                ExpressionSyntax noerror = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.WIN32_ERROR"), IdentifierName("NO_ERROR"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, noerror);
                                break;
                            default:
                                throw new NotSupportedException($"Return type {identifierName.Identifier.ValueText} on release method {releaseMethod} not supported.");
                        }

                        break;
                }
            }

            MethodDeclarationSyntax releaseHandleDeclaration = MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier("ReleaseHandle"))
                .AddModifiers(TokenWithSpace(SyntaxKind.ProtectedKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword));
            releaseHandleDeclaration = releaseBlock is null
                ? releaseHandleDeclaration
                     .WithExpressionBody(ArrowExpressionClause(releaseInvocation))
                     .WithSemicolonToken(SemicolonWithLineFeed)
                : releaseHandleDeclaration
                    .WithBody(releaseBlock);
            members.Add(releaseHandleDeclaration);

            ClassDeclarationSyntax safeHandleDeclaration = ClassDeclaration(Identifier(safeHandleClassName))
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(SafeHandleTypeSyntax))))
                .AddMembers(members.ToArray())
                .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute))
                .WithLeadingTrivia(ParseLeadingTrivia($@"
/// <summary>
/// Represents a Win32 handle that can be closed with <see cref=""{this.options.ClassName}.{renamedReleaseMethod ?? releaseMethod}""/>.
/// </summary>
"));

            this.volatileCode.AddSafeHandleType(safeHandleDeclaration);
            return safeHandleType;
        }
        catch (Exception ex)
        {
            throw new GenerationFailedException($"Failed while generating SafeHandle for {releaseMethod}.", ex);
        }
    }

    internal bool TryGetTypeDefFieldType(TypeHandleInfo? typeDef, [NotNullWhen(true)] out TypeHandleInfo? fieldType)
    {
        if (typeDef is HandleTypeHandleInfo handle)
        {
            switch (handle.Handle.Kind)
            {
                case HandleKind.TypeReference:
                    if (this.TryGetTypeDefHandle((TypeReferenceHandle)handle.Handle, out TypeDefinitionHandle tdh))
                    {
                        return Resolve(tdh, out fieldType);
                    }

                    break;
                case HandleKind.TypeDefinition:
                    return Resolve((TypeDefinitionHandle)handle.Handle, out fieldType);
            }
        }

        bool Resolve(TypeDefinitionHandle tdh, [NotNullWhen(true)] out TypeHandleInfo? fieldType)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            foreach (FieldDefinitionHandle fdh in td.GetFields())
            {
                FieldDefinition fd = this.Reader.GetFieldDefinition(fdh);
                fieldType = fd.DecodeSignature(SignatureHandleProvider.Instance, null);
                return true;
            }

            fieldType = null;
            return false;
        }

        fieldType = default;
        return false;
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

        // Skip if the compilation already defines this type or can access it from elsewhere.
        if (this.FindTypeSymbolIfAlreadyAvailable(fullyQualifiedName) is object)
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

                specialDeclaration = specialDeclaration.WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, subNamespace));

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

    internal NativeArrayInfo? FindNativeArrayInfoAttribute(CustomAttributeHandleCollection customAttributeHandles)
    {
        return this.FindInteropDecorativeAttribute(customAttributeHandles, NativeArrayInfoAttribute) is CustomAttribute att
            ? DecodeNativeArrayInfoAttribute(att)
            : null;
    }

    internal CustomAttribute? FindInteropDecorativeAttribute(CustomAttributeHandleCollection? customAttributeHandles, string attributeName)
        => this.FindAttribute(customAttributeHandles, InteropDecorationNamespace, attributeName);

    internal CustomAttribute? FindAttribute(CustomAttributeHandleCollection? customAttributeHandles, string attributeNamespace, string attributeName)
        => MetadataUtilities.FindAttribute(this.Reader, customAttributeHandles, attributeNamespace, attributeName);

    internal bool TryGetTypeDefHandle(TypeReferenceHandle typeRefHandle, out QualifiedTypeDefinitionHandle typeDefHandle)
    {
        if (this.SuperGenerator is object)
        {
            return this.SuperGenerator.TryGetTypeDefinitionHandle(new QualifiedTypeReferenceHandle(this, typeRefHandle), out typeDefHandle);
        }

        if (this.MetadataIndex.TryGetTypeDefHandle(this.Reader, typeRefHandle, out TypeDefinitionHandle localTypeDefHandle))
        {
            typeDefHandle = new QualifiedTypeDefinitionHandle(this, localTypeDefHandle);
            return true;
        }

        typeDefHandle = default;
        return false;
    }

    internal bool TryGetTypeDefHandle(TypeReferenceHandle typeRefHandle, out TypeDefinitionHandle typeDefHandle) => this.MetadataIndex.TryGetTypeDefHandle(this.Reader, typeRefHandle, out typeDefHandle);

    internal bool TryGetTypeDefHandle(TypeReference typeRef, out TypeDefinitionHandle typeDefHandle) => this.TryGetTypeDefHandle(typeRef.Namespace, typeRef.Name, out typeDefHandle);

    internal bool TryGetTypeDefHandle(StringHandle @namespace, StringHandle name, out TypeDefinitionHandle typeDefHandle)
    {
        // PERF: Use an index
        foreach (TypeDefinitionHandle tdh in this.Reader.TypeDefinitions)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            if (td.Name.Equals(name) && td.Namespace.Equals(@namespace))
            {
                typeDefHandle = tdh;
                return true;
            }
        }

        typeDefHandle = default;
        return false;
    }

    internal bool TryGetTypeDefHandle(string @namespace, string name, out TypeDefinitionHandle typeDefinitionHandle)
    {
        // PERF: Use an index
        foreach (TypeDefinitionHandle tdh in this.Reader.TypeDefinitions)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            if (this.Reader.StringComparer.Equals(td.Name, name) && this.Reader.StringComparer.Equals(td.Namespace, @namespace))
            {
                typeDefinitionHandle = tdh;
                return true;
            }
        }

        typeDefinitionHandle = default;
        return false;
    }

    internal bool IsNonCOMInterface(TypeDefinition interfaceTypeDef)
    {
        if (this.Reader.StringComparer.Equals(interfaceTypeDef.Name, "IUnknown"))
        {
            return false;
        }

        // A conforming interface must have IUnknown as or an ancestor of its first base type.
        InterfaceImplementationHandle firstBaseInterface = interfaceTypeDef.GetInterfaceImplementations().FirstOrDefault();
        if (firstBaseInterface.IsNil)
        {
            return true;
        }

        InterfaceImplementation baseIFace = this.Reader.GetInterfaceImplementation(firstBaseInterface);
        TypeDefinitionHandle baseIFaceTypeDefHandle;
        if (baseIFace.Interface.Kind == HandleKind.TypeDefinition)
        {
            baseIFaceTypeDefHandle = (TypeDefinitionHandle)baseIFace.Interface;
        }
        else if (baseIFace.Interface.Kind == HandleKind.TypeReference)
        {
            if (!this.TryGetTypeDefHandle((TypeReferenceHandle)baseIFace.Interface, out baseIFaceTypeDefHandle))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return this.IsNonCOMInterface(this.Reader.GetTypeDefinition(baseIFaceTypeDefHandle));
    }

    internal bool IsNonCOMInterface(TypeReferenceHandle interfaceTypeRefHandle) => this.TryGetTypeDefHandle(interfaceTypeRefHandle, out TypeDefinitionHandle tdh) && this.IsNonCOMInterface(this.Reader.GetTypeDefinition(tdh));

    internal FunctionPointerTypeSyntax FunctionPointer(TypeDefinition delegateType)
    {
        CustomAttribute ufpAtt = this.FindAttribute(delegateType.GetCustomAttributes(), SystemRuntimeInteropServices, nameof(UnmanagedFunctionPointerAttribute))!.Value;
        CustomAttributeValue<TypeSyntax> attArgs = ufpAtt.DecodeValue(CustomAttributeTypeProvider.Instance);
        var callingConvention = (CallingConvention)attArgs.FixedArguments[0].Value!;

        this.GetSignatureForDelegate(delegateType, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes);
        if (this.FindAttribute(returnTypeAttributes, SystemRuntimeInteropServices, nameof(MarshalAsAttribute)).HasValue)
        {
            throw new NotSupportedException("Marshaling is not supported for function pointers.");
        }

        return this.FunctionPointer(invokeMethodDef, signature);
    }

    internal bool IsDelegate(TypeDefinition typeDef) => (typeDef.Attributes & TypeAttributes.Class) == TypeAttributes.Class && typeDef.BaseType.Kind == HandleKind.TypeReference && this.Reader.StringComparer.Equals(this.Reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType).Name, nameof(MulticastDelegate));

    internal bool IsManagedType(TypeHandleInfo typeHandleInfo)
    {
        TypeHandleInfo elementType =
            typeHandleInfo is PointerTypeHandleInfo ptr ? ptr.ElementType :
            typeHandleInfo is ArrayTypeHandleInfo array ? array.ElementType :
            typeHandleInfo;
        if (elementType is PointerTypeHandleInfo ptr2)
        {
            return this.IsManagedType(ptr2.ElementType);
        }
        else if (elementType is PrimitiveTypeHandleInfo)
        {
            return false;
        }
        else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeDefinition } typeDefHandle })
        {
            return this.IsManagedType((TypeDefinitionHandle)typeDefHandle);
        }
        else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeReference } typeRefHandle } handleElement)
        {
            var trh = (TypeReferenceHandle)typeRefHandle;
            if (this.TryGetTypeDefHandle(trh, out TypeDefinitionHandle tdr))
            {
                return this.IsManagedType(tdr);
            }

            // If the type comes from an external assembly, assume that structs are blittable and anything else is not.
            TypeReference tr = this.Reader.GetTypeReference(trh);
            if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference && handleElement.RawTypeKind is byte kind)
            {
                // Structs set 0x1, classes set 0x2.
                return (kind & 0x1) == 0;
            }
        }

        throw new GenerationFailedException("Unrecognized type: " + elementType.GetType().Name);
    }

    internal bool HasUnmanagedSuffix(bool allowMarshaling, bool isManagedType) => !allowMarshaling && isManagedType && this.options.AllowMarshaling;

    internal string GetMangledIdentifier(string normalIdentifier, bool allowMarshaling, bool isManagedType) =>
        this.HasUnmanagedSuffix(allowMarshaling, isManagedType) ? normalIdentifier + UnmanagedInteropSuffix : normalIdentifier;

    internal TypeSyntax GetMangledIdentifier(TypeSyntax normalIdentifier, bool allowMarshaling, bool isManagedType)
    {
        if (this.HasUnmanagedSuffix(allowMarshaling, isManagedType))
        {
            return normalIdentifier is QualifiedNameSyntax qname ? QualifiedName(qname.Left, IdentifierName(qname.Right.Identifier.ValueText + UnmanagedInteropSuffix)) :
                normalIdentifier is SimpleNameSyntax simpleName ? IdentifierName(simpleName.Identifier.ValueText + UnmanagedInteropSuffix) :
                throw new NotSupportedException(normalIdentifier.GetType().Name);
        }

        return normalIdentifier;
    }

    internal SyntaxToken GetMangledIdentifier(SyntaxToken normalIdentifier, bool allowMarshaling, bool isManagedType)
    {
        if (this.HasUnmanagedSuffix(allowMarshaling, isManagedType))
        {
            string mangledName = normalIdentifier.ValueText + UnmanagedInteropSuffix;
            return Identifier(normalIdentifier.LeadingTrivia, mangledName, normalIdentifier.TrailingTrivia);
        }

        return normalIdentifier;
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

    private static IntPtr GetPreferredInvalidHandleValue(HashSet<IntPtr> invalidHandleValues) => invalidHandleValues.Contains(new IntPtr(-1)) ? new IntPtr(-1) : invalidHandleValues.FirstOrDefault();

    private static bool RequiresUnsafe(TypeSyntax? typeSyntax) => typeSyntax is PointerTypeSyntax || typeSyntax is FunctionPointerTypeSyntax;

    private static string GetClassNameForModule(string moduleName) =>
        moduleName.StartsWith("api-", StringComparison.Ordinal) || moduleName.StartsWith("ext-", StringComparison.Ordinal) ? "ApiSets" : moduleName.Replace('-', '_');

    private static SyntaxToken SafeIdentifier(string name) => SafeIdentifierName(name).Identifier;

    private static IdentifierNameSyntax SafeIdentifierName(string name) => IdentifierName(CSharpKeywords.Contains(name) ? "@" + name : name);

    private static string GetHiddenFieldName(string fieldName) => $"__{fieldName}";

    private static bool IsWideFunction(string methodName)
    {
        if (methodName.Length > 1 && methodName.EndsWith("W", StringComparison.Ordinal) && char.IsLower(methodName[methodName.Length - 2]))
        {
            // The name looks very much like an Wide-char method.
            // If further confidence is ever needed, we could look at the parameter and return types
            // to see if they have charset-related metadata in their marshaling metadata.
            return true;
        }

        return false;
    }

    private static bool IsAnsiFunction(string methodName)
    {
        if (methodName.Length > 1 && methodName.EndsWith("A", StringComparison.Ordinal) && char.IsLower(methodName[methodName.Length - 2]))
        {
            // The name looks very much like an Ansi method.
            // If further confidence is ever needed, we could look at the parameter and return types
            // to see if they have charset-related metadata in their marshaling metadata.
            return true;
        }

        return false;
    }

    private static ObjectCreationExpressionSyntax PropertyKeyValue(CustomAttribute propertyKeyAttribute, TypeSyntax type)
    {
        CustomAttributeValue<TypeSyntax> args = propertyKeyAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        uint a = (uint)args.FixedArguments[0].Value!;
        ushort b = (ushort)args.FixedArguments[1].Value!;
        ushort c = (ushort)args.FixedArguments[2].Value!;
        byte d = (byte)args.FixedArguments[3].Value!;
        byte e = (byte)args.FixedArguments[4].Value!;
        byte f = (byte)args.FixedArguments[5].Value!;
        byte g = (byte)args.FixedArguments[6].Value!;
        byte h = (byte)args.FixedArguments[7].Value!;
        byte i = (byte)args.FixedArguments[8].Value!;
        byte j = (byte)args.FixedArguments[9].Value!;
        byte k = (byte)args.FixedArguments[10].Value!;
        uint pid = (uint)args.FixedArguments[11].Value!;

        return ObjectCreationExpression(type).WithInitializer(
            InitializerExpression(SyntaxKind.ObjectInitializerExpression, SeparatedList<ExpressionSyntax>(new[]
            {
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("fmtid"), GuidValue(propertyKeyAttribute)),
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("pid"), LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(pid))),
            })));
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

    private static string? FetchTemplateText(string name)
    {
        using Stream? templateStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.templates.{name.Replace('/', '.')}.cs");
        if (templateStream is null)
        {
            return null;
        }

        using StreamReader sr = new(templateStream);
        return sr.ReadToEnd().Replace("\r\n", "\n").Replace("\t", string.Empty);
    }

    private static bool TryFetchTemplate(string name, Generator? generator, [NotNullWhen(true)] out MemberDeclarationSyntax? member)
    {
        string? template = FetchTemplateText(name);
        if (template == null)
        {
            member = null;
            return false;
        }

        member = ParseMemberDeclaration(template, generator?.parseOptions) ?? throw new GenerationFailedException($"Unable to parse a type from a template: {name}");

        // Strip out #if/#else/#endif trivia, which was already evaluated with the parse options we passed in.
        if (generator?.parseOptions is not null)
        {
            member = (MemberDeclarationSyntax)member.Accept(DirectiveTriviaRemover.Instance)!;
        }

        member = generator?.ElevateVisibility(member) ?? member;
        return true;
    }

    private static bool IsLibraryAllowedAppLocal(string libraryName)
    {
        for (int i = 0; i < AppLocalLibraries.Length; i++)
        {
            if (string.Equals(libraryName, AppLocalLibraries[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Guid DecodeGuidFromAttribute(CustomAttribute guidAttribute)
    {
        CustomAttributeValue<TypeSyntax> args = guidAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        return new Guid(
            (uint)args.FixedArguments[0].Value!,
            (ushort)args.FixedArguments[1].Value!,
            (ushort)args.FixedArguments[2].Value!,
            (byte)args.FixedArguments[3].Value!,
            (byte)args.FixedArguments[4].Value!,
            (byte)args.FixedArguments[5].Value!,
            (byte)args.FixedArguments[6].Value!,
            (byte)args.FixedArguments[7].Value!,
            (byte)args.FixedArguments[8].Value!,
            (byte)args.FixedArguments[9].Value!,
            (byte)args.FixedArguments[10].Value!);
    }

    private static bool IsHresult(TypeHandleInfo? typeHandleInfo) => typeHandleInfo is HandleTypeHandleInfo handleInfo && handleInfo.IsType("HRESULT");

    private T AddApiDocumentation<T>(string api, T memberDeclaration)
        where T : MemberDeclarationSyntax
    {
        if (this.ApiDocs is object && this.ApiDocs.TryGetApiDocs(api, out ApiDetails? docs))
        {
            var docCommentsBuilder = new StringBuilder();
            if (docs.Description is object)
            {
                docCommentsBuilder.Append($@"/// <summary>");
                EmitDoc(docs.Description, docCommentsBuilder, docs, string.Empty);
                docCommentsBuilder.AppendLine("</summary>");
            }

            if (docs.Parameters is object)
            {
                if (memberDeclaration is BaseMethodDeclarationSyntax methodDecl)
                {
                    foreach (KeyValuePair<string, string> entry in docs.Parameters)
                    {
                        if (!methodDecl.ParameterList.Parameters.Any(p => string.Equals(p.Identifier.ValueText, entry.Key, StringComparison.Ordinal)))
                        {
                            // Skip documentation for parameters that do not actually exist on the method.
                            continue;
                        }

                        docCommentsBuilder.Append($@"/// <param name=""{entry.Key}"">");
                        EmitDoc(entry.Value, docCommentsBuilder, docs, "parameters");
                        docCommentsBuilder.AppendLine("</param>");
                    }
                }
            }

            if (docs.Fields is object)
            {
                var fieldsDocBuilder = new StringBuilder();
                switch (memberDeclaration)
                {
                    case StructDeclarationSyntax structDeclaration:
                        memberDeclaration = memberDeclaration.ReplaceNodes(
                            structDeclaration.Members.OfType<FieldDeclarationSyntax>(),
                            (_, field) =>
                            {
                                VariableDeclaratorSyntax? variable = field.Declaration.Variables.Single();
                                if (docs.Fields.TryGetValue(variable.Identifier.ValueText, out string? fieldDoc))
                                {
                                    fieldsDocBuilder.Append("/// <summary>");
                                    EmitDoc(fieldDoc, fieldsDocBuilder, docs, "members");
                                    fieldsDocBuilder.AppendLine("</summary>");
                                    if (field.Declaration.Type.HasAnnotations(OriginalDelegateAnnotation))
                                    {
                                        fieldsDocBuilder.AppendLine(@$"/// <remarks>See the <see cref=""{field.Declaration.Type.GetAnnotations(OriginalDelegateAnnotation).Single().Data}"" /> delegate for more about this function.</remarks>");
                                    }

                                    field = field.WithLeadingTrivia(ParseLeadingTrivia(fieldsDocBuilder.ToString().Replace("\r\n", "\n")));
                                    fieldsDocBuilder.Clear();
                                }

                                return field;
                            });
                        break;
                    case EnumDeclarationSyntax enumDeclaration:
                        memberDeclaration = memberDeclaration.ReplaceNodes(
                            enumDeclaration.Members,
                            (_, field) =>
                            {
                                if (docs.Fields.TryGetValue(field.Identifier.ValueText, out string? fieldDoc))
                                {
                                    fieldsDocBuilder.Append($@"/// <summary>");
                                    EmitDoc(fieldDoc, fieldsDocBuilder, docs, "members");
                                    fieldsDocBuilder.AppendLine("</summary>");
                                    field = field.WithLeadingTrivia(ParseLeadingTrivia(fieldsDocBuilder.ToString().Replace("\r\n", "\n")));
                                    fieldsDocBuilder.Clear();
                                }

                                return field;
                            });
                        break;
                }
            }

            if (docs.ReturnValue is object)
            {
                docCommentsBuilder.Append("/// <returns>");
                EmitDoc(docs.ReturnValue, docCommentsBuilder, docs: null, string.Empty);
                docCommentsBuilder.AppendLine("</returns>");
            }

            if (docs.Remarks is object || docs.HelpLink is object)
            {
                docCommentsBuilder.Append($"/// <remarks>");
                if (docs.Remarks is object)
                {
                    EmitDoc(docs.Remarks, docCommentsBuilder, docs, string.Empty);
                }
                else if (docs.HelpLink is object)
                {
                    docCommentsBuilder.AppendLine();
                    docCommentsBuilder.AppendLine($@"/// <para><see href=""{docs.HelpLink}"">Learn more about this API from docs.microsoft.com</see>.</para>");
                    docCommentsBuilder.Append("/// ");
                }

                docCommentsBuilder.AppendLine($"</remarks>");
            }

            memberDeclaration = memberDeclaration.WithLeadingTrivia(
                ParseLeadingTrivia(docCommentsBuilder.ToString().Replace("\r\n", "\n")));
        }

        return memberDeclaration;

        static void EmitLine(StringBuilder stringBuilder, string yamlDocSrc)
        {
            stringBuilder.Append(yamlDocSrc.Trim());
        }

        static void EmitDoc(string yamlDocSrc, StringBuilder docCommentsBuilder, ApiDetails? docs, string docsAnchor)
        {
            if (yamlDocSrc.Contains('\n'))
            {
                docCommentsBuilder.AppendLine();
                var docReader = new StringReader(yamlDocSrc);
                string? paramDocLine;

                bool inParagraph = false;
                bool inComment = false;
                int blankLineCounter = 0;
                while ((paramDocLine = docReader.ReadLine()) is object)
                {
                    if (string.IsNullOrWhiteSpace(paramDocLine))
                    {
                        if (++blankLineCounter >= 2 && inParagraph)
                        {
                            docCommentsBuilder.AppendLine("</para>");
                            inParagraph = false;
                            inComment = false;
                        }

                        continue;
                    }
                    else if (blankLineCounter > 0)
                    {
                        blankLineCounter = 0;
                    }
                    else if (docCommentsBuilder.Length > 0 && docCommentsBuilder[docCommentsBuilder.Length - 1] != '\n')
                    {
                        docCommentsBuilder.Append(' ');
                    }

                    if (inParagraph)
                    {
                        if (docCommentsBuilder.Length > 0 && docCommentsBuilder[docCommentsBuilder.Length - 1] is not (' ' or '\n'))
                        {
                            docCommentsBuilder.Append(' ');
                        }
                    }
                    else
                    {
                        docCommentsBuilder.Append("/// <para>");
                        inParagraph = true;
                        inComment = true;
                    }

                    if (!inComment)
                    {
                        docCommentsBuilder.Append("/// ");
                    }

                    if (paramDocLine.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("```", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<<", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // We don't try to format tables, so truncate at this point.
                        if (inParagraph)
                        {
                            docCommentsBuilder.AppendLine("</para>");
                            inParagraph = false;
                            inComment = false;
                        }

                        docCommentsBuilder.AppendLine($@"/// <para>This doc was truncated.</para>");

                        break; // is this the right way?
                    }

                    EmitLine(docCommentsBuilder, paramDocLine);
                }

                if (inParagraph)
                {
                    if (!inComment)
                    {
                        docCommentsBuilder.Append("/// ");
                    }

                    docCommentsBuilder.AppendLine("</para>");
                    inParagraph = false;
                    inComment = false;
                }

                if (docs is object)
                {
                    docCommentsBuilder.AppendLine($@"/// <para><see href=""{docs.HelpLink}#{docsAnchor}"">Read more on docs.microsoft.com</see>.</para>");
                }

                docCommentsBuilder.Append("/// ");
            }
            else
            {
                EmitLine(docCommentsBuilder, yamlDocSrc);
            }
        }
    }

    private MemberDeclarationSyntax FetchTemplate(string name)
    {
        if (!this.TryFetchTemplate(name, out MemberDeclarationSyntax? result))
        {
            throw new KeyNotFoundException();
        }

        return result;
    }

    private bool TryFetchTemplate(string name, [NotNullWhen(true)] out MemberDeclarationSyntax? member) => TryFetchTemplate(name, this, out member);

    private HashSet<IntPtr> GetInvalidHandleValues(EntityHandle handle)
    {
        QualifiedTypeDefinitionHandle tdh;
        if (handle.Kind == HandleKind.TypeReference)
        {
            if (!this.TryGetTypeDefHandle((TypeReferenceHandle)handle, out tdh))
            {
                throw new GenerationFailedException("Unable to look up type definition.");
            }
        }
        else if (handle.Kind == HandleKind.TypeDefinition)
        {
            tdh = new QualifiedTypeDefinitionHandle(this, (TypeDefinitionHandle)handle);
        }
        else
        {
            throw new GenerationFailedException("Unexpected handle type.");
        }

        HashSet<IntPtr> invalidHandleValues = new();
        QualifiedTypeDefinition td = tdh.Resolve();
        foreach (CustomAttributeHandle ah in td.Definition.GetCustomAttributes())
        {
            CustomAttribute a = td.Reader.GetCustomAttribute(ah);
            if (MetadataUtilities.IsAttribute(td.Reader, a, InteropDecorationNamespace, InvalidHandleValueAttribute))
            {
                CustomAttributeValue<TypeSyntax> attributeData = a.DecodeValue(CustomAttributeTypeProvider.Instance);
                long invalidValue = (long)(attributeData.FixedArguments[0].Value ?? throw new GenerationFailedException("Missing invalid value attribute."));
                invalidHandleValues.Add((IntPtr)invalidValue);
            }
        }

        return invalidHandleValues;
    }

    private FunctionPointerTypeSyntax FunctionPointer(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature)
    {
        FunctionPointerCallingConventionSyntax callingConventionSyntax = FunctionPointerCallingConvention(
            Token(SyntaxKind.UnmanagedKeyword),
            FunctionPointerUnmanagedCallingConventionList(SingletonSeparatedList(ToUnmanagedCallingConventionSyntax(CallingConvention.StdCall))));

        FunctionPointerParameterListSyntax parametersList = FunctionPointerParameterList();

        foreach (ParameterHandle parameterHandle in methodDefinition.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == 0)
            {
                continue;
            }

            TypeHandleInfo? parameterTypeInfo = signature.ParameterTypes[parameter.SequenceNumber - 1];
            parametersList = parametersList.AddParameters(this.TranslateDelegateToFunctionPointer(parameterTypeInfo, parameter.GetCustomAttributes()));
        }

        parametersList = parametersList.AddParameters(this.TranslateDelegateToFunctionPointer(signature.ReturnType, this.GetReturnTypeCustomAttributes(methodDefinition)));

        return FunctionPointerType(callingConventionSyntax, parametersList);
    }

    private FunctionPointerParameterSyntax TranslateDelegateToFunctionPointer(TypeHandleInfo parameterTypeInfo, CustomAttributeHandleCollection? customAttributeHandles)
    {
        if (this.IsDelegateReference(parameterTypeInfo, out TypeDefinition delegateTypeDef))
        {
            return FunctionPointerParameter(this.FunctionPointer(delegateTypeDef));
        }

        return FunctionPointerParameter(parameterTypeInfo.ToTypeSyntax(this.functionPointerTypeSettings, customAttributeHandles).GetUnmarshaledType());
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

    private CustomAttributeHandleCollection? GetReturnTypeCustomAttributes(MethodDefinition methodDefinition)
    {
        CustomAttributeHandleCollection? returnTypeAttributes = null;
        foreach (ParameterHandle parameterHandle in methodDefinition.GetParameters())
        {
            Parameter parameter = this.Reader.GetParameter(parameterHandle);
            if (parameter.Name.IsNil)
            {
                returnTypeAttributes = parameter.GetCustomAttributes();
            }

            // What we're looking for would always be the first element in the collection.
            break;
        }

        return returnTypeAttributes;
    }

    private bool IsCompilerGenerated(TypeDefinition typeDef) => this.FindAttribute(typeDef.GetCustomAttributes(), SystemRuntimeCompilerServices, nameof(CompilerGeneratedAttribute)).HasValue;

    private bool HasObsoleteAttribute(CustomAttributeHandleCollection attributes) => this.FindAttribute(attributes, nameof(System), nameof(ObsoleteAttribute)).HasValue;

    private ISymbol? FindTypeSymbolIfAlreadyAvailable(string fullyQualifiedMetadataName)
    {
        if (this.findTypeSymbolIfAlreadyAvailableCache.TryGetValue(fullyQualifiedMetadataName, out ISymbol? result))
        {
            return result;
        }

        if (this.compilation is object)
        {
            if (this.compilation.Assembly.GetTypeByMetadataName(fullyQualifiedMetadataName) is { } ownSymbol)
            {
                // This assembly defines it.
                // But if it defines it as a partial, we should not consider it as fully defined so we populate our side.
                result = ownSymbol.DeclaringSyntaxReferences.Any(sr => sr.GetSyntax() is BaseTypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword))
                    ? null
                    : ownSymbol;
                this.findTypeSymbolIfAlreadyAvailableCache.Add(fullyQualifiedMetadataName, result);
                return result;
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
                            // If we already found a match, then we have multiple matches now and the compiler won't be able to resolve our type references.
                            // In such a case, we'll prefer to just declare our own local symbol.
                            if (result is not null)
                            {
                                this.findTypeSymbolIfAlreadyAvailableCache.Add(fullyQualifiedMetadataName, null);
                                return null;
                            }

                            result = externalSymbol;
                        }
                    }
                }
            }
        }

        this.findTypeSymbolIfAlreadyAvailableCache.Add(fullyQualifiedMetadataName, result);
        return result;
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

        if (this.FindTypeSymbolIfAlreadyAvailable(fullyQualifiedName) is object)
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
                    typeDeclaration = this.DeclareCocreatableClass(typeDef);
                }
                else
                {
                    StructDeclarationSyntax structDeclaration = this.DeclareStruct(typeDefHandle, context);

                    // Proactively generate all nested types as well.
                    foreach (TypeDefinitionHandle nestedHandle in typeDef.GetNestedTypes())
                    {
                        if (this.RequestInteropTypeHelper(nestedHandle, context) is { } nestedType)
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
                    this.options.AllowMarshaling ? this.DeclareDelegate(typeDef) :
                    null;
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
            throw new GenerationFailedException("Failed to generate " + this.Reader.GetString(typeDef.Name), ex);
        }
    }

    private bool IsUntypedDelegate(TypeDefinition typeDef) => IsUntypedDelegate(this.Reader, typeDef);

    private bool IsTypeDefStruct(TypeDefinition typeDef) => this.FindInteropDecorativeAttribute(typeDef.GetCustomAttributes(), NativeTypedefAttribute).HasValue;

    private bool IsEmptyStructWithGuid(TypeDefinition typeDef)
    {
        return this.FindInteropDecorativeAttribute(typeDef.GetCustomAttributes(), nameof(GuidAttribute)).HasValue
            && typeDef.GetFields().Count == 0;
    }

    private void DeclareExternMethod(MethodDefinitionHandle methodDefinitionHandle)
    {
        MethodDefinition methodDefinition = this.Reader.GetMethodDefinition(methodDefinitionHandle);
        MethodImport import = methodDefinition.GetImport();
        if (import.Name.IsNil)
        {
            // Not an exported method.
            return;
        }

        string? methodName = this.Reader.GetString(methodDefinition.Name);
        try
        {
            if (this.WideCharOnly && IsAnsiFunction(methodName))
            {
                // Skip Ansi functions.
                return;
            }

            string? moduleName = this.GetNormalizedModuleName(import);

            string? entrypoint = null;
            if (this.TryGetRenamedMethod(methodName, out string? newName))
            {
                entrypoint = methodName;
                methodName = newName;
            }

            // If this method releases a handle, recreate the method signature such that we take the struct rather than the SafeHandle as a parameter.
            TypeSyntaxSettings typeSettings = this.MetadataIndex.ReleaseMethods.Contains(entrypoint ?? methodName) ? this.externReleaseSignatureTypeSettings : this.externSignatureTypeSettings;
            MethodSignature<TypeHandleInfo> signature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);
            bool requiresUnicodeCharSet = signature.ParameterTypes.Any(p => p is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Char });

            CustomAttributeHandleCollection? returnTypeAttributes = this.GetReturnTypeCustomAttributes(methodDefinition);
            TypeSyntaxAndMarshaling returnType = signature.ReturnType.ToTypeSyntax(typeSettings, returnTypeAttributes, ParameterAttributes.Out);

            MethodDeclarationSyntax methodDeclaration = MethodDeclaration(
                List<AttributeListSyntax>()
                    .Add(AttributeList()
                        .WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken))
                        .AddAttributes(DllImport(import, moduleName, entrypoint, requiresUnicodeCharSet ? CharSet.Unicode : CharSet.Ansi))),
                modifiers: TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.ExternKeyword)),
                returnType.Type.WithTrailingTrivia(TriviaList(Space)),
                explicitInterfaceSpecifier: null!,
                SafeIdentifier(methodName),
                null!,
                FixTrivia(this.CreateParameterList(methodDefinition, signature, typeSettings)),
                List<TypeParameterConstraintClauseSyntax>(),
                body: null!,
                TokenWithLineFeed(SyntaxKind.SemicolonToken));
            methodDeclaration = returnType.AddReturnMarshalAs(methodDeclaration);

            if (this.generateDefaultDllImportSearchPathsAttribute)
            {
                methodDeclaration = methodDeclaration.AddAttributeLists(
                    IsLibraryAllowedAppLocal(moduleName) ? DefaultDllImportSearchPathsAllowAppDirAttributeList : DefaultDllImportSearchPathsAttributeList);
            }

            if (this.GetSupportedOSPlatformAttribute(methodDefinition.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
            {
                methodDeclaration = methodDeclaration.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
            }

            // Add documentation if we can find it.
            methodDeclaration = this.AddApiDocumentation(entrypoint ?? methodName, methodDeclaration);

            if (RequiresUnsafe(methodDeclaration.ReturnType) || methodDeclaration.ParameterList.Parameters.Any(p => RequiresUnsafe(p.Type)))
            {
                methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            this.volatileCode.AddMemberToModule(moduleName, this.DeclareFriendlyOverloads(methodDefinition, methodDeclaration, this.methodsAndConstantsClassName, FriendlyOverloadOf.ExternMethod, this.injectedPInvokeHelperMethods));
            this.volatileCode.AddMemberToModule(moduleName, methodDeclaration);
        }
        catch (Exception ex)
        {
            throw new GenerationFailedException($"Failed while generating extern method: {methodName}", ex);
        }
    }

    private bool IsCompatibleWithPlatform(CustomAttributeHandleCollection customAttributesOnMember) => MetadataUtilities.IsCompatibleWithPlatform(this.Reader, this.MetadataIndex, this.compilation?.Options.Platform, customAttributesOnMember);

    private AttributeSyntax? GetSupportedOSPlatformAttribute(CustomAttributeHandleCollection attributes)
    {
        AttributeSyntax? supportedOSPlatformAttribute = null;
        if (this.generateSupportedOSPlatformAttributes && this.FindInteropDecorativeAttribute(attributes, "SupportedOSPlatformAttribute") is CustomAttribute templateOSPlatformAttribute)
        {
            CustomAttributeValue<TypeSyntax> args = templateOSPlatformAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
            supportedOSPlatformAttribute = SupportedOSPlatformAttributeSyntax.AddArgumentListArguments(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal((string)args.FixedArguments[0].Value!))));
        }

        return supportedOSPlatformAttribute;
    }

    /// <summary>
    /// Searches for an extern method.
    /// </summary>
    /// <param name="possiblyQualifiedName">A simple method name or one qualified with a namespace.</param>
    /// <param name="exactNameMatchOnly"><see langword="true"/> to only match on an exact method name; <see langword="false"/> to allow for fuzzy matching such as an omitted W or A suffix.</param>
    /// <returns>The matching method if exactly one is found, or <see langword="null"/> if none was found.</returns>
    /// <exception cref="ArgumentException">Thrown if the <paramref name="possiblyQualifiedName"/> argument is not qualified and more than one matching method name was found.</exception>
    private MethodDefinitionHandle? GetMethodByName(string possiblyQualifiedName, bool exactNameMatchOnly = false)
    {
        TrySplitPossiblyQualifiedName(possiblyQualifiedName, out string? methodNamespace, out string methodName);
        return this.GetMethodByName(methodNamespace, methodName, exactNameMatchOnly);
    }

    /// <summary>
    /// Searches for an extern method.
    /// </summary>
    /// <param name="methodNamespace">The namespace the method is found in, if known.</param>
    /// <param name="methodName">The simple name of the method.</param>
    /// <param name="exactNameMatchOnly"><see langword="true"/> to only match on an exact method name; <see langword="false"/> to allow for fuzzy matching such as an omitted W or A suffix.</param>
    /// <returns>The matching method if exactly one is found, or <see langword="null"/> if none was found.</returns>
    private MethodDefinitionHandle? GetMethodByName(string? methodNamespace, string methodName, bool exactNameMatchOnly = false)
    {
        IEnumerable<NamespaceMetadata> namespaces = this.GetNamespacesToSearch(methodNamespace);
        bool foundApiWithMismatchedPlatform = false;

        var matchingMethodHandles = new List<MethodDefinitionHandle>();
        foreach (NamespaceMetadata? nsMetadata in namespaces)
        {
            if (nsMetadata.Methods.TryGetValue(methodName, out MethodDefinitionHandle handle))
            {
                matchingMethodHandles.Add(handle);
            }
            else if (nsMetadata.MethodsForOtherPlatform.Contains(methodName))
            {
                foundApiWithMismatchedPlatform = true;
            }
        }

        if (!exactNameMatchOnly && matchingMethodHandles.Count == 0)
        {
            foreach (NamespaceMetadata? nsMetadata in namespaces)
            {
                if (nsMetadata.Methods.TryGetValue(methodName + "W", out MethodDefinitionHandle handle) ||
                    nsMetadata.Methods.TryGetValue(methodName + "A", out handle))
                {
                    matchingMethodHandles.Add(handle);
                }
            }
        }

        if (matchingMethodHandles.Count == 1)
        {
            return matchingMethodHandles[0];
        }
        else if (matchingMethodHandles.Count > 1)
        {
            string matches = string.Join(
                ", ",
                matchingMethodHandles.Select(h =>
                {
                    MethodDefinition md = this.Reader.GetMethodDefinition(h);
                    TypeDefinition td = this.Reader.GetTypeDefinition(md.GetDeclaringType());
                    return $"{this.Reader.GetString(td.Namespace)}.{this.Reader.GetString(md.Name)}";
                }));
            throw new ArgumentException("The method name is ambiguous. Use the fully-qualified name instead. Possible matches: " + matches);
        }

        if (foundApiWithMismatchedPlatform)
        {
            throw new PlatformIncompatibleException($"The requested API ({methodName}) was found but is not available given the target platform ({this.compilation?.Options.Platform}).");
        }

        return null;
    }

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

    private FieldDeclarationSyntax DeclareConstant(FieldDefinition fieldDef)
    {
        string name = this.Reader.GetString(fieldDef.Name);
        try
        {
            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null) with { IsConstantField = true };
            CustomAttributeHandleCollection customAttributes = fieldDef.GetCustomAttributes();
            TypeSyntaxAndMarshaling fieldType = fieldTypeInfo.ToTypeSyntax(this.fieldTypeSettings, customAttributes);
            ExpressionSyntax value =
                fieldDef.GetDefaultValue() is { IsNil: false } constantHandle ? this.ToExpressionSyntax(this.Reader.GetConstant(constantHandle)) :
                this.FindInteropDecorativeAttribute(customAttributes, nameof(GuidAttribute)) is CustomAttribute guidAttribute ? GuidValue(guidAttribute) :
                this.FindInteropDecorativeAttribute(customAttributes, "PropertyKeyAttribute") is CustomAttribute propertyKeyAttribute ? PropertyKeyValue(propertyKeyAttribute, fieldType.Type) :
                throw new NotSupportedException("Unsupported constant: " + name);
            bool requiresUnsafe = false;
            if (fieldType.Type is not PredefinedTypeSyntax && value is not ObjectCreationExpressionSyntax)
            {
                if (fieldTypeInfo is HandleTypeHandleInfo handleFieldTypeInfo && this.IsHandle(handleFieldTypeInfo.Handle, out _))
                {
                    // Cast to IntPtr first, then the actual handle struct.
                    value = CastExpression(fieldType.Type, CastExpression(IntPtrTypeSyntax, ParenthesizedExpression(value)));
                }
                else if (fieldType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCSTR" } } })
                {
                    value = CastExpression(fieldType.Type, CastExpression(PointerType(PredefinedType(Token(SyntaxKind.ByteKeyword))), ParenthesizedExpression(value)));
                    requiresUnsafe = true;
                }
                else if (fieldType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } })
                {
                    value = CastExpression(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))), ParenthesizedExpression(value));
                    requiresUnsafe = true;
                }
                else
                {
                    value = CastExpression(fieldType.Type, ParenthesizedExpression(value));
                }
            }

            SyntaxTokenList modifiers = TokenList(TokenWithSpace(this.Visibility));
            if (this.IsTypeDefStruct(fieldTypeInfo) || value is ObjectCreationExpressionSyntax)
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.StaticKeyword)).Add(TokenWithSpace(SyntaxKind.ReadOnlyKeyword));
            }
            else
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.ConstKeyword));
            }

            if (requiresUnsafe)
            {
                modifiers = modifiers.Add(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            FieldDeclarationSyntax? result = FieldDeclaration(VariableDeclaration(fieldType.Type).AddVariables(
                VariableDeclarator(Identifier(name)).WithInitializer(EqualsValueClause(value))))
                .WithModifiers(modifiers);
            result = fieldType.AddMarshalAs(result);
            result = this.AddApiDocumentation(result.Declaration.Variables[0].Identifier.ValueText, result);

            return result;
        }
        catch (Exception ex)
        {
            TypeDefinition typeDef = this.Reader.GetTypeDefinition(fieldDef.GetDeclaringType());
            string typeName = this.Reader.GetString(typeDef.Name);
            string? ns = this.Reader.GetString(typeDef.Namespace);
            throw new GenerationFailedException($"Failed creating field: {ns}.{typeName}.{name}", ex);
        }
    }

    private ClassDeclarationSyntax DeclareConstantDefiningClass()
    {
        return ClassDeclaration(this.methodsAndConstantsClassName.Identifier)
            .AddMembers(this.committedCode.TopLevelFields.ToArray())
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)));
    }

    private ClassDeclarationSyntax DeclareInlineArrayIndexerExtensionsClass()
    {
        return ClassDeclaration(InlineArrayIndexerExtensionsClassName.Identifier)
            .AddMembers(this.committedCode.InlineArrayIndexerExtensions.ToArray())
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)))
            .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute));
    }

    /// <summary>
    /// Generates a type to represent a COM interface.
    /// </summary>
    /// <param name="typeDefHandle">The type definition handle of the interface.</param>
    /// <param name="context">The generation context.</param>
    /// <returns>The type declaration.</returns>
    /// <remarks>
    /// COM interfaces are represented as structs in order to maintain the "unmanaged type" trait
    /// so that all structs are blittable.
    /// </remarks>
    private TypeDeclarationSyntax? DeclareInterface(TypeDefinitionHandle typeDefHandle, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        var baseTypes = ImmutableStack.Create<QualifiedTypeDefinitionHandle>();
        (Generator Generator, InterfaceImplementationHandle Handle) baseTypeHandle = (this, typeDef.GetInterfaceImplementations().SingleOrDefault());
        while (!baseTypeHandle.Handle.IsNil)
        {
            InterfaceImplementation baseTypeImpl = baseTypeHandle.Generator.Reader.GetInterfaceImplementation(baseTypeHandle.Handle);
            if (!baseTypeHandle.Generator.TryGetTypeDefHandle((TypeReferenceHandle)baseTypeImpl.Interface, out QualifiedTypeDefinitionHandle baseTypeDefHandle))
            {
                throw new GenerationFailedException("Failed to find base type.");
            }

            baseTypes = baseTypes.Push(baseTypeDefHandle);
            TypeDefinition baseType = baseTypeDefHandle.Reader.GetTypeDefinition(baseTypeDefHandle.DefinitionHandle);
            baseTypeHandle = (baseTypeHandle.Generator, baseType.GetInterfaceImplementations().SingleOrDefault());
        }

        if (this.IsNonCOMInterface(typeDef))
        {
            // We cannot declare an interface that is not COM-compliant.
            return this.DeclareInterfaceAsStruct(typeDefHandle, baseTypes, context);
        }

        if (context.AllowMarshaling)
        {
            // Marshaling is allowed here, and generally. Just emit the interface.
            return this.DeclareInterfaceAsInterface(typeDef, baseTypes, context);
        }

        // Marshaling of this interface is not allowed here. Emit the struct.
        TypeDeclarationSyntax structDecl = this.DeclareInterfaceAsStruct(typeDefHandle, baseTypes, context);
        if (!this.options.AllowMarshaling)
        {
            // Marshaling isn't allowed over the entire compilation, so emit the interface nested under the struct so
            // it can be implemented and enable CCW scenarios.
            TypeDeclarationSyntax? ifaceDecl = this.DeclareInterfaceAsInterface(typeDef, baseTypes, context, interfaceAsSubtype: true);
            if (ifaceDecl is not null)
            {
                structDecl = structDecl.AddMembers(ifaceDecl);
            }
        }

        return structDecl;
    }

    private TypeDeclarationSyntax DeclareInterfaceAsStruct(TypeDefinitionHandle typeDefHandle, ImmutableStack<QualifiedTypeDefinitionHandle> baseTypes, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        IdentifierNameSyntax ifaceName = IdentifierName(this.GetMangledIdentifier(this.Reader.GetString(typeDef.Name), context.AllowMarshaling, isManagedType: true));
        IdentifierNameSyntax vtblFieldName = IdentifierName("lpVtbl");
        var members = new List<MemberDeclarationSyntax>();
        var vtblMembers = new List<MemberDeclarationSyntax>();
        TypeSyntaxSettings typeSettings = this.comSignatureTypeSettings;

        // It is imperative that we generate methods for all base interfaces as well, ahead of any implemented by *this* interface.
        var allMethods = new List<QualifiedMethodDefinitionHandle>();
        while (!baseTypes.IsEmpty)
        {
            QualifiedTypeDefinitionHandle qualifiedBaseType = baseTypes.Peek();
            baseTypes = baseTypes.Pop();
            TypeDefinition baseType = qualifiedBaseType.Generator.Reader.GetTypeDefinition(qualifiedBaseType.DefinitionHandle);
            allMethods.AddRange(baseType.GetMethods().Select(m => new QualifiedMethodDefinitionHandle(qualifiedBaseType.Generator, m)));
        }

        allMethods.AddRange(typeDef.GetMethods().Select(m => new QualifiedMethodDefinitionHandle(this, m)));
        int methodCounter = 0;
        HashSet<string> helperMethodsInStruct = new();
        ISet<string> declaredProperties = this.GetDeclarableProperties(
            allMethods.Select(qh => qh.Reader.GetMethodDefinition(qh.MethodHandle)),
            allowNonConsecutiveAccessors: true);
        foreach (QualifiedMethodDefinitionHandle methodDefHandle in allMethods)
        {
            methodCounter++;
            QualifiedMethodDefinition methodDefinition = methodDefHandle.Resolve();
            string methodName = methodDefinition.Reader.GetString(methodDefinition.Method.Name);
            IdentifierNameSyntax innerMethodName = IdentifierName($"{methodName}_{methodCounter}");
            LiteralExpressionSyntax methodOffset = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(methodCounter - 1));

            MethodSignature<TypeHandleInfo> signature = methodDefinition.Method.DecodeSignature(SignatureHandleProvider.Instance, null);
            CustomAttributeHandleCollection? returnTypeAttributes = methodDefinition.Generator.GetReturnTypeCustomAttributes(methodDefinition.Method);
            TypeSyntaxAndMarshaling returnType = signature.ReturnType.ToTypeSyntax(typeSettings, returnTypeAttributes);

            ParameterListSyntax parameterList = methodDefinition.Generator.CreateParameterList(methodDefinition.Method, signature, typeSettings);
            FunctionPointerParameterListSyntax funcPtrParameters = FunctionPointerParameterList()
                .AddParameters(FunctionPointerParameter(PointerType(ifaceName)))
                .AddParameters(parameterList.Parameters.Select(p => FunctionPointerParameter(p.Type!).WithModifiers(p.Modifiers)).ToArray())
                .AddParameters(FunctionPointerParameter(returnType.Type));

            TypeSyntax unmanagedDelegateType = FunctionPointerType().WithCallingConvention(
                FunctionPointerCallingConvention(TokenWithSpace(SyntaxKind.UnmanagedKeyword))
                    .WithUnmanagedCallingConventionList(FunctionPointerUnmanagedCallingConventionList(
                        SingletonSeparatedList(FunctionPointerUnmanagedCallingConvention(Identifier("Stdcall"))))))
                .WithParameterList(funcPtrParameters);
            FieldDeclarationSyntax vtblFunctionPtr = FieldDeclaration(
                VariableDeclaration(unmanagedDelegateType)
                .WithVariables(SingletonSeparatedList(VariableDeclarator(innerMethodName.Identifier))))
                .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.InternalKeyword)));
            vtblMembers.Add(vtblFunctionPtr);

            // Build up an unmanaged delegate cast directly from the vtbl pointer and invoke it.
            // By doing this, we make the emitted code more trimmable by not referencing the full virtual method table and its full set of types
            // when the app may only invoke a subset of the methods.
            IdentifierNameSyntax pThisLocal = IdentifierName("pThis");
            ExpressionSyntax vtblIndexingExpression = ParenthesizedExpression(
                CastExpression(unmanagedDelegateType, ElementAccessExpression(vtblFieldName).AddArgumentListArguments(Argument(methodOffset))));
            InvocationExpressionSyntax vtblInvocation = InvocationExpression(vtblIndexingExpression)
                .WithArgumentList(FixTrivia(ArgumentList()
                    .AddArguments(Argument(pThisLocal))
                    .AddArguments(parameterList.Parameters.Select(p => Argument(IdentifierName(p.Identifier.ValueText)).WithRefKindKeyword(p.Modifiers.Count > 0 ? p.Modifiers[0] : default)).ToArray())));

            MemberDeclarationSyntax propertyOrMethod;
            MethodDeclarationSyntax? methodDeclaration = null;

            // We can declare this method as a property accessor if it represents a property.
            // We must also confirm that the property type is the same in both cases, because sometimes they aren't (e.g. IUIAutomationProxyFactoryEntry.ClassName).
            if (this.TryGetPropertyAccessorInfo(methodDefinition.Method, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType) &&
                declaredProperties.Contains(propertyName.Identifier.ValueText))
            {
                StatementSyntax ThrowOnHRFailure(ExpressionSyntax hrExpression) => ExpressionStatement(InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, hrExpression, IdentifierName("ThrowOnFailure")),
                    ArgumentList()));

                BlockSyntax? body;
                switch (accessorKind)
                {
                    case SyntaxKind.GetAccessorDeclaration:
                        // PropertyType __result;
                        IdentifierNameSyntax resultLocal = IdentifierName("__result");
                        LocalDeclarationStatementSyntax resultLocalDeclaration = LocalDeclarationStatement(VariableDeclaration(propertyType).AddVariables(VariableDeclarator(resultLocal.Identifier)));

                        // vtblInvoke(pThis, &__result).ThrowOnFailure();
                        // vtblInvoke(pThis, out __result).ThrowOnFailure();
                        ArgumentSyntax resultArgument = funcPtrParameters.Parameters[1].Modifiers.Any(SyntaxKind.OutKeyword)
                            ? Argument(resultLocal).WithRefKindKeyword(Token(SyntaxKind.OutKeyword))
                            : Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, resultLocal));
                        StatementSyntax vtblInvocationStatement = ThrowOnHRFailure(vtblInvocation.WithArgumentList(ArgumentList().AddArguments(Argument(pThisLocal), resultArgument)));

                        // return __result;
                        StatementSyntax returnStatement = ReturnStatement(resultLocal);

                        body = Block().AddStatements(
                            FixedStatement(
                                VariableDeclaration(PointerType(ifaceName)).AddVariables(
                                    VariableDeclarator(pThisLocal.Identifier).WithInitializer(EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ThisExpression())))),
                                Block().AddStatements(
                                    resultLocalDeclaration,
                                    vtblInvocationStatement,
                                    returnStatement)).WithFixedKeyword(TokenWithSpace(SyntaxKind.FixedKeyword)));
                        break;
                    case SyntaxKind.SetAccessorDeclaration:
                        // vtblInvoke(pThis, value).ThrowOnFailure();
                        vtblInvocationStatement = ThrowOnHRFailure(vtblInvocation.WithArgumentList(ArgumentList().AddArguments(Argument(pThisLocal), Argument(IdentifierName("value")))));
                        body = Block().AddStatements(
                            FixedStatement(
                                VariableDeclaration(PointerType(ifaceName)).AddVariables(
                                    VariableDeclarator(pThisLocal.Identifier).WithInitializer(EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ThisExpression())))),
                                vtblInvocationStatement).WithFixedKeyword(TokenWithSpace(SyntaxKind.FixedKeyword)));
                        break;
                    default:
                        throw new NotSupportedException("Unsupported accessor kind: " + accessorKind);
                }

                AccessorDeclarationSyntax accessor = AccessorDeclaration(accessorKind.Value, body);

                int priorPropertyDeclarationIndex = members.FindIndex(m => m is PropertyDeclarationSyntax prop && prop.Identifier.ValueText == propertyName.Identifier.ValueText);
                if (priorPropertyDeclarationIndex >= 0)
                {
                    // Add the accessor to the existing property declaration.
                    PropertyDeclarationSyntax priorDeclaration = (PropertyDeclarationSyntax)members[priorPropertyDeclarationIndex];
                    members[priorPropertyDeclarationIndex] = priorDeclaration.WithAccessorList(priorDeclaration.AccessorList!.AddAccessors(accessor));
                    continue;
                }
                else
                {
                    PropertyDeclarationSyntax propertyDeclaration = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), propertyName.Identifier.WithTrailingTrivia(LineFeed));

                    propertyDeclaration = propertyDeclaration
                        .WithAccessorList(AccessorList().AddAccessors(accessor))
                        .AddModifiers(Token(this.Visibility));

                    if (propertyDeclaration.Type is PointerTypeSyntax)
                    {
                        propertyDeclaration = propertyDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                    }

                    propertyOrMethod = propertyDeclaration;
                }
            }
            else
            {
                StatementSyntax vtblInvocationStatement = IsVoid(returnType.Type)
                    ? ExpressionStatement(vtblInvocation)
                    : ReturnStatement(vtblInvocation);
                BlockSyntax? body = Block().AddStatements(
                    FixedStatement(
                        VariableDeclaration(PointerType(ifaceName)).AddVariables(
                            VariableDeclarator(pThisLocal.Identifier).WithInitializer(EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ThisExpression())))),
                        vtblInvocationStatement).WithFixedKeyword(TokenWithSpace(SyntaxKind.FixedKeyword)));

                methodDeclaration = MethodDeclaration(
                    List<AttributeListSyntax>(),
                    modifiers: TokenList(TokenWithSpace(SyntaxKind.PublicKeyword)), // always use public so struct can implement the COM interface
                    returnType.Type.WithTrailingTrivia(TriviaList(Space)),
                    explicitInterfaceSpecifier: null!,
                    SafeIdentifier(methodName),
                    null!,
                    parameterList,
                    List<TypeParameterConstraintClauseSyntax>(),
                    body: body,
                    semicolonToken: default);
                methodDeclaration = returnType.AddReturnMarshalAs(methodDeclaration);

                if (methodName == nameof(object.GetType) && parameterList.Parameters.Count == 0)
                {
                    methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
                }

                if (methodDeclaration.ReturnType is PointerTypeSyntax || methodDeclaration.ParameterList.Parameters.Any(p => p.Type is PointerTypeSyntax))
                {
                    methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                }

                propertyOrMethod = methodDeclaration;

                members.AddRange(methodDefinition.Generator.DeclareFriendlyOverloads(methodDefinition.Method, methodDeclaration, IdentifierName(ifaceName.Identifier.ValueText), FriendlyOverloadOf.StructMethod, helperMethodsInStruct));
            }

            // Add documentation if we can find it.
            propertyOrMethod = this.AddApiDocumentation($"{ifaceName}.{methodName}", propertyOrMethod);
            members.Add(propertyOrMethod);
        }

        // We expose the vtbl struct, not because we expect folks to use it directly, but because some folks may use it to manually generate CCWs.
        StructDeclarationSyntax? vtblStruct = StructDeclaration(Identifier("Vtbl"))
            .AddMembers(vtblMembers.ToArray())
            .AddModifiers(TokenWithSpace(this.Visibility));
        members.Add(vtblStruct);

        // private void** lpVtbl; // Vtbl* (but we avoid strong typing to enable trimming the entire vtbl struct away)
        members.Add(FieldDeclaration(VariableDeclaration(PointerType(PointerType(PredefinedType(Token(SyntaxKind.VoidKeyword))))).AddVariables(VariableDeclarator(vtblFieldName.Identifier))).AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword)));

        BaseListSyntax baseList = BaseList(SeparatedList<BaseTypeSyntax>());

        CustomAttribute? guidAttribute = this.FindGuidAttribute(typeDef.GetCustomAttributes());
        var staticMembers = this.DeclareStaticCOMInterfaceMembers(guidAttribute);
        members.AddRange(staticMembers.Members);
        baseList = baseList.AddTypes(staticMembers.BaseTypes.ToArray());

        StructDeclarationSyntax iface = StructDeclaration(ifaceName.Identifier)
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.PartialKeyword))
            .AddMembers(members.ToArray());

        if (baseList.Types.Count > 0)
        {
            iface = iface.WithBaseList(baseList);
        }

        if (guidAttribute.HasValue)
        {
            iface = iface.AddAttributeLists(AttributeList().AddAttributes(GUID(DecodeGuidFromAttribute(guidAttribute.Value))));
        }

        if (this.GetSupportedOSPlatformAttribute(typeDef.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
        {
            iface = iface.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
        }

        return iface;
    }

    private TypeDeclarationSyntax? DeclareInterfaceAsInterface(TypeDefinition typeDef, ImmutableStack<QualifiedTypeDefinitionHandle> baseTypes, Context context, bool interfaceAsSubtype = false)
    {
        if (this.Reader.StringComparer.Equals(typeDef.Name, "IUnknown") || this.Reader.StringComparer.Equals(typeDef.Name, "IDispatch"))
        {
            // We do not generate interfaces for these COM base types.
            return null;
        }

        string actualIfaceName = this.Reader.GetString(typeDef.Name);
        IdentifierNameSyntax ifaceName = interfaceAsSubtype ? NestedCOMInterfaceName : IdentifierName(actualIfaceName);
        TypeSyntaxSettings typeSettings = this.comSignatureTypeSettings;

        // It is imperative that we generate methods for all base interfaces as well, ahead of any implemented by *this* interface.
        var allMethods = new List<MethodDefinitionHandle>();
        bool foundIUnknown = false;
        bool foundIDispatch = false;
        bool foundIInspectable = false;
        var baseTypeSyntaxList = new List<BaseTypeSyntax>();
        while (!baseTypes.IsEmpty)
        {
            QualifiedTypeDefinitionHandle baseTypeHandle = baseTypes.Peek();
            baseTypes = baseTypes.Pop();
            TypeDefinition baseType = baseTypeHandle.Reader.GetTypeDefinition(baseTypeHandle.DefinitionHandle);
            if (!foundIUnknown)
            {
                if (!baseTypeHandle.Reader.StringComparer.Equals(baseType.Name, "IUnknown"))
                {
                    throw new NotSupportedException("Unsupported base COM interface type: " + baseTypeHandle.Reader.GetString(baseType.Name));
                }

                foundIUnknown = true;
            }
            else
            {
                if (baseTypeHandle.Reader.StringComparer.Equals(baseType.Name, "IDispatch"))
                {
                    foundIDispatch = true;
                }
                else if (baseTypeHandle.Reader.StringComparer.Equals(baseType.Name, "IInspectable"))
                {
                    foundIInspectable = true;
                }
                else
                {
                    baseTypeHandle.Generator.RequestInteropType(baseTypeHandle.DefinitionHandle, context);
                    TypeSyntax baseTypeSyntax = new HandleTypeHandleInfo(baseTypeHandle.Reader, baseTypeHandle.DefinitionHandle).ToTypeSyntax(this.comSignatureTypeSettings, null).Type;
                    if (interfaceAsSubtype)
                    {
                        baseTypeSyntax = QualifiedName(
                            baseTypeSyntax is PointerTypeSyntax baseTypePtr ? (NameSyntax)baseTypePtr.ElementType : (NameSyntax)baseTypeSyntax,
                            NestedCOMInterfaceName);
                    }

                    baseTypeSyntaxList.Add(SimpleBaseType(baseTypeSyntax));
                    allMethods.AddRange(baseType.GetMethods());
                }
            }
        }

        int inheritedMethods = allMethods.Count;
        allMethods.AddRange(typeDef.GetMethods());

        AttributeSyntax ifaceType = InterfaceType(
            foundIInspectable ? ComInterfaceType.InterfaceIsIInspectable :
            foundIDispatch ? ComInterfaceType.InterfaceIsIDispatch :
            foundIUnknown ? ComInterfaceType.InterfaceIsIUnknown :
            throw new NotSupportedException("No COM interface base type found."));

        var members = new List<MemberDeclarationSyntax>();
        var friendlyOverloads = new List<MethodDeclarationSyntax>();
        ISet<string> declaredProperties = this.GetDeclarableProperties(allMethods.Select(this.Reader.GetMethodDefinition), allowNonConsecutiveAccessors: false);

        foreach (MethodDefinitionHandle methodDefHandle in allMethods)
        {
            MethodDefinition methodDefinition = this.Reader.GetMethodDefinition(methodDefHandle);
            string methodName = this.Reader.GetString(methodDefinition.Name);
            inheritedMethods--;
            try
            {
                MemberDeclarationSyntax propertyOrMethod;
                MethodDeclarationSyntax? methodDeclaration = null;

                // Consider whether we should declare this as a property.
                // Even if it could be represented as a property accessor, we cannot do so if a property by the same name was already declared in anything other than the previous row.
                // Adding an accessor to a property later than the very next row would screw up the virtual method table ordering.
                // We must also confirm that the property type is the same in both cases, because sometimes they aren't (e.g. IUIAutomationProxyFactoryEntry.ClassName).
                if (this.TryGetPropertyAccessorInfo(methodDefinition, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType) && declaredProperties.Contains(propertyName.Identifier.ValueText))
                {
                    AccessorDeclarationSyntax accessor = AccessorDeclaration(accessorKind.Value).WithSemicolonToken(Semicolon);

                    if (members.Count > 0 && members[members.Count - 1] is PropertyDeclarationSyntax lastProperty && lastProperty.Identifier.ValueText == propertyName.Identifier.ValueText)
                    {
                        // Add the accessor to the existing property declaration.
                        members[members.Count - 1] = lastProperty.WithAccessorList(lastProperty.AccessorList!.AddAccessors(accessor));
                        continue;
                    }
                    else
                    {
                        PropertyDeclarationSyntax propertyDeclaration = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), propertyName.Identifier.WithTrailingTrivia(LineFeed));

                        propertyDeclaration = propertyDeclaration.WithAccessorList(AccessorList().AddAccessors(accessor));

                        if (propertyDeclaration.Type is PointerTypeSyntax)
                        {
                            propertyDeclaration = propertyDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                        }

                        propertyOrMethod = propertyDeclaration;
                    }
                }
                else
                {
                    MethodSignature<TypeHandleInfo> signature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);

                    CustomAttributeHandleCollection? returnTypeAttributes = this.GetReturnTypeCustomAttributes(methodDefinition);
                    TypeSyntaxAndMarshaling returnTypeDetails = signature.ReturnType.ToTypeSyntax(typeSettings, returnTypeAttributes);
                    TypeSyntax returnType = returnTypeDetails.Type;
                    AttributeSyntax? returnsAttribute = MarshalAs(returnTypeDetails.MarshalAsAttribute, returnTypeDetails.NativeArrayInfo);

                    ParameterListSyntax? parameterList = this.CreateParameterList(methodDefinition, signature, this.comSignatureTypeSettings);

                    bool preserveSig = interfaceAsSubtype
                        || !IsHresult(signature.ReturnType)
                        || (methodDefinition.ImplAttributes & MethodImplAttributes.PreserveSig) == MethodImplAttributes.PreserveSig
                        || this.FindInteropDecorativeAttribute(methodDefinition.GetCustomAttributes(), CanReturnMultipleSuccessValuesAttribute) is not null
                        || this.FindInteropDecorativeAttribute(methodDefinition.GetCustomAttributes(), CanReturnErrorsAsSuccessAttribute) is not null
                        || this.options.ComInterop.PreserveSigMethods.Contains($"{ifaceName}.{methodName}")
                        || this.options.ComInterop.PreserveSigMethods.Contains(ifaceName.ToString());

                    if (!preserveSig)
                    {
                        ParameterSyntax? lastParameter = parameterList.Parameters.Count > 0 ? parameterList.Parameters[parameterList.Parameters.Count - 1] : null;
                        if (lastParameter?.HasAnnotation(IsRetValAnnotation) is true)
                        {
                            // Move the retval parameter to the return value position.
                            parameterList = parameterList.WithParameters(parameterList.Parameters.RemoveAt(parameterList.Parameters.Count - 1));
                            returnType = lastParameter.Modifiers.Any(SyntaxKind.OutKeyword) ? lastParameter.Type! : ((PointerTypeSyntax)lastParameter.Type!).ElementType;
                            returnsAttribute = lastParameter.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(att => att.Name.ToString() == "MarshalAs");
                        }
                        else
                        {
                            // Remove the return type
                            returnType = PredefinedType(Token(SyntaxKind.VoidKeyword));
                        }
                    }

                    methodDeclaration = MethodDeclaration(returnType.WithTrailingTrivia(TriviaList(Space)), SafeIdentifier(methodName))
                        .WithParameterList(FixTrivia(parameterList))
                        .WithSemicolonToken(SemicolonWithLineFeed);
                    if (returnsAttribute is object)
                    {
                        methodDeclaration = methodDeclaration.AddAttributeLists(
                            AttributeList().WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword))).AddAttributes(returnsAttribute));
                    }

                    if (preserveSig)
                    {
                        methodDeclaration = methodDeclaration.AddAttributeLists(AttributeList().AddAttributes(PreserveSigAttributeSyntax));
                    }

                    if (methodDeclaration.ReturnType is PointerTypeSyntax || methodDeclaration.ParameterList.Parameters.Any(p => p.Type is PointerTypeSyntax))
                    {
                        methodDeclaration = methodDeclaration.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                    }

                    propertyOrMethod = methodDeclaration;
                }

                if (inheritedMethods >= 0)
                {
                    propertyOrMethod = propertyOrMethod.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
                }

                // Add documentation if we can find it.
                propertyOrMethod = this.AddApiDocumentation($"{ifaceName}.{methodName}", propertyOrMethod);
                members.Add(propertyOrMethod);

                if (methodDeclaration is not null)
                {
                    NameSyntax declaringTypeName = HandleTypeHandleInfo.GetNestingQualifiedName(this, this.Reader, typeDef, hasUnmanagedSuffix: false, isInterfaceNestedInStruct: interfaceAsSubtype);
                    friendlyOverloads.AddRange(
                        this.DeclareFriendlyOverloads(methodDefinition, methodDeclaration, declaringTypeName, FriendlyOverloadOf.InterfaceMethod, this.injectedPInvokeHelperMethodsToFriendlyOverloadsExtensions));
                }
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException($"Failed while generating the method: {methodName}", ex);
            }
        }

        CustomAttribute? guidAttribute = this.FindGuidAttribute(typeDef.GetCustomAttributes());

        InterfaceDeclarationSyntax ifaceDeclaration = InterfaceDeclaration(ifaceName.Identifier)
            .WithKeyword(TokenWithSpace(SyntaxKind.InterfaceKeyword))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .AddMembers(members.ToArray());

        if (guidAttribute.HasValue)
        {
            ifaceDeclaration = ifaceDeclaration.AddAttributeLists(AttributeList().AddAttributes(GUID(DecodeGuidFromAttribute(guidAttribute.Value)), ifaceType, ComImportAttributeSyntax));
        }

        if (baseTypeSyntaxList.Count > 0)
        {
            ifaceDeclaration = ifaceDeclaration
                .WithBaseList(BaseList(SeparatedList(baseTypeSyntaxList)));
        }

        if (this.generateSupportedOSPlatformAttributesOnInterfaces && this.GetSupportedOSPlatformAttribute(typeDef.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
        {
            ifaceDeclaration = ifaceDeclaration.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
        }

        // Only add overloads to instance collections after everything else is done,
        // so we don't leave extension methods behind if we fail to generate the target interface.
        if (friendlyOverloads.Count > 0)
        {
            string ns = this.Reader.GetString(typeDef.Namespace);
            if (this.TryStripCommonNamespace(ns, out string? strippedNamespace))
            {
                ns = strippedNamespace;
            }

            ClassDeclarationSyntax friendlyOverloadClass = ClassDeclaration(Identifier($"{ns.Replace('.', '_')}_{actualIfaceName}_Extensions"))
                .WithMembers(List<MemberDeclarationSyntax>(friendlyOverloads))
                .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)))
                .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute));
            this.volatileCode.AddComInterfaceExtension(friendlyOverloadClass);
        }

        return ifaceDeclaration;
    }

    private unsafe (List<MemberDeclarationSyntax> Members, List<BaseTypeSyntax> BaseTypes) DeclareStaticCOMInterfaceMembers(CustomAttribute? guidAttribute)
    {
        List<MemberDeclarationSyntax> members = new();
        List<BaseTypeSyntax> baseTypes = new();

        if (guidAttribute.HasValue)
        {
            Guid guidAttributeValue = DecodeGuidFromAttribute(guidAttribute.Value);

            // internal static readonly Guid IID_Guid = new Guid(0x1234, ...);
            IdentifierNameSyntax iidGuidFieldName = IdentifierName("IID_Guid");
            TypeSyntax guidTypeSyntax = IdentifierName(nameof(Guid));
            members.Add(FieldDeclaration(
                VariableDeclaration(guidTypeSyntax)
                .AddVariables(VariableDeclarator(iidGuidFieldName.Identifier).WithInitializer(EqualsValueClause(
                    GuidValue(guidAttribute.Value)))))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>The IID guid for this interface.</summary>\n/// <value>{guidAttributeValue:B}</value>\n")));

            if (this.TryDeclareCOMGuidInterfaceIfNecessary())
            {
                baseTypes.Add(SimpleBaseType(IComIIDGuidInterfaceName));

                IdentifierNameSyntax dataLocal = IdentifierName("data");

                // Rather than just `return ref IID_Guid`, which returns a pointer to a 'movable' field,
                // We leverage C# syntax that we know the modern C# compiler will turn into a pointer directly into the dll image,
                // so that the pointer does not move.
                // This does rely on at least the generated code running on a little endian machine, since we're laying raw bytes on top of integer fields.
                // But at the moment, we also assume this source generator is running on little endian for convenience for the reverse operation.
                if (!BitConverter.IsLittleEndian)
                {
                    throw new NotSupportedException("Conversion from big endian to little endian is not implemented.");
                }

                // ReadOnlySpan<byte> data = new byte[] { ... };
                ReadOnlySpan<byte> guidBytes = new((byte*)&guidAttributeValue, sizeof(Guid));
                LocalDeclarationStatementSyntax dataDecl = LocalDeclarationStatement(
                    VariableDeclaration(MakeReadOnlySpanOfT(PredefinedType(Token(SyntaxKind.ByteKeyword)))).AddVariables(
                        VariableDeclarator(dataLocal.Identifier).WithInitializer(EqualsValueClause(NewByteArray(guidBytes)))));

                // return ref Unsafe.As<byte, Guid>(ref MemoryMarshal.GetReference(data));
                ReturnStatementSyntax returnStatement = ReturnStatement(RefExpression(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(nameof(Unsafe)),
                            GenericName(nameof(Unsafe.As)).AddTypeArgumentListArguments(PredefinedType(Token(SyntaxKind.ByteKeyword)), IdentifierName(nameof(Guid)))),
                        ArgumentList().AddArguments(
                            Argument(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(MemoryMarshal)), IdentifierName(nameof(MemoryMarshal.GetReference))),
                                    ArgumentList(SingletonSeparatedList(Argument(dataLocal)))))
                            .WithRefOrOutKeyword(Token(SyntaxKind.RefKeyword))))));

                // The native assembly code for this property getter is just a `mov` and a `ret.
                // For our callers to also enjoy just the `mov` instruction, we have to attribute for aggressive inlining.
                // [MethodImpl(MethodImplOptions.AggressiveInlining)]
                AttributeListSyntax methodImplAttr = AttributeList().AddAttributes(MethodImpl(MethodImplOptions.AggressiveInlining));

                BlockSyntax getBody = Block(dataDecl, returnStatement);

                // static ref readonly Guid IComIID.Guid { get { ... } }
                PropertyDeclarationSyntax guidProperty = PropertyDeclaration(IdentifierName(nameof(Guid)).WithTrailingTrivia(Space), ComIIDGuidPropertyName.Identifier)
                    .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(IComIIDGuidInterfaceName))
                    .AddModifiers(TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.RefKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .WithAccessorList(AccessorList().AddAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, getBody).AddAttributeLists(methodImplAttr)));
                members.Add(guidProperty);
            }
        }

        return (members, baseTypes);
    }

    private ISet<string> GetDeclarableProperties(IEnumerable<MethodDefinition> methods, bool allowNonConsecutiveAccessors)
    {
        Dictionary<string, (TypeSyntax Type, int Index)> goodProperties = new(StringComparer.Ordinal);
        HashSet<string> badProperties = new(StringComparer.Ordinal);
        int rowIndex = -1;
        foreach (MethodDefinition methodDefinition in methods)
        {
            rowIndex++;
            if (this.TryGetPropertyAccessorInfo(methodDefinition, out IdentifierNameSyntax? propertyName, out SyntaxKind? accessorKind, out TypeSyntax? propertyType))
            {
                if (badProperties.Contains(propertyName.Identifier.ValueText))
                {
                    continue;
                }

                if (goodProperties.TryGetValue(propertyName.Identifier.ValueText, out var priorPropertyData))
                {
                    bool badProperty = false;
                    badProperty |= priorPropertyData.Type.ToString() != propertyType.ToString();
                    badProperty |= !allowNonConsecutiveAccessors && priorPropertyData.Index != rowIndex - 1;
                    if (badProperty)
                    {
                        ReportBadProperty();
                        continue;
                    }
                }

                goodProperties[propertyName.Identifier.ValueText] = (propertyType, rowIndex);
            }
            else if (propertyName is not null)
            {
                ReportBadProperty();
            }

            void ReportBadProperty()
            {
                badProperties.Add(propertyName.Identifier.ValueText);
                goodProperties.Remove(propertyName.Identifier.ValueText);
            }
        }

        return goodProperties.Count == 0 ? ImmutableHashSet<string>.Empty : new HashSet<string>(goodProperties.Keys, StringComparer.Ordinal);
    }

    private bool TryGetPropertyAccessorInfo(MethodDefinition methodDefinition, [NotNullWhen(true)] out IdentifierNameSyntax? propertyName, [NotNullWhen(true)] out SyntaxKind? accessorKind, [NotNullWhen(true)] out TypeSyntax? propertyType)
    {
        propertyName = null;
        accessorKind = null;
        propertyType = null;

        if ((methodDefinition.Attributes & MethodAttributes.SpecialName) != MethodAttributes.SpecialName)
        {
            // Sometimes another method actually *does* qualify as a property accessor, which would have this method as another accessor if it qualified.
            // But since this doesn't qualify, produce the property name that should be disqualified as a whole since C# doesn't like seeing property X and method set_X as seperate declarations.
            string disqualifiedMethodName = this.Reader.GetString(methodDefinition.Name);
            int index = disqualifiedMethodName.IndexOf('_');
            if (index > 0)
            {
                propertyName = IdentifierName(disqualifiedMethodName.Substring(index + 1));
            }

            return false;
        }

        if ((methodDefinition.ImplAttributes & MethodImplAttributes.PreserveSig) == MethodImplAttributes.PreserveSig)
        {
            return false;
        }

        ParameterHandleCollection parameters = methodDefinition.GetParameters();
        if (parameters.Count != 2)
        {
            return false;
        }

        string methodName = this.Reader.GetString(methodDefinition.Name);
        const string getterPrefix = "get_";
        const string setterPrefix = "put_";
        bool isGetter = methodName.StartsWith(getterPrefix, StringComparison.Ordinal);
        bool isSetter = methodName.StartsWith(setterPrefix, StringComparison.Ordinal);

        if (isGetter || isSetter)
        {
            MethodSignature<TypeHandleInfo> signature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);
            if (!IsHresult(signature.ReturnType))
            {
                return false;
            }

            Parameter propertyTypeParameter = this.Reader.GetParameter(parameters.Skip(1).Single());
            propertyType = signature.ParameterTypes[0].ToTypeSyntax(this.comSignatureTypeSettings, propertyTypeParameter.GetCustomAttributes(), propertyTypeParameter.Attributes).Type;

            if (isGetter)
            {
                propertyName = SafeIdentifierName(methodName.Substring(getterPrefix.Length));
                accessorKind = SyntaxKind.GetAccessorDeclaration;

                if ((propertyTypeParameter.Attributes & ParameterAttributes.Out) != ParameterAttributes.Out)
                {
                    return false;
                }

                if (propertyType is PointerTypeSyntax propertyTypePointer)
                {
                    propertyType = propertyTypePointer.ElementType;
                }

                return true;
            }

            if (isSetter)
            {
                propertyName = SafeIdentifierName(methodName.Substring(setterPrefix.Length));
                accessorKind = SyntaxKind.SetAccessorDeclaration;
                return true;
            }
        }

        return false;
    }

    private CustomAttribute? FindGuidAttribute(CustomAttributeHandleCollection attributes) => this.FindInteropDecorativeAttribute(attributes, nameof(GuidAttribute));

    private Guid? FindGuidFromAttribute(TypeDefinition typeDef) => this.FindGuidFromAttribute(typeDef.GetCustomAttributes());

    private Guid? FindGuidFromAttribute(CustomAttributeHandleCollection attributes) => this.FindGuidAttribute(attributes) is CustomAttribute att ? (Guid?)DecodeGuidFromAttribute(att) : null;

    private DelegateDeclarationSyntax DeclareDelegate(TypeDefinition typeDef)
    {
        if (!this.options.AllowMarshaling)
        {
            throw new NotSupportedException("Delegates are not declared while in all-structs mode.");
        }

        string name = this.Reader.GetString(typeDef.Name);
        TypeSyntaxSettings typeSettings = this.delegateSignatureTypeSettings;

        CallingConvention? callingConvention = null;
        if (this.FindAttribute(typeDef.GetCustomAttributes(), SystemRuntimeInteropServices, nameof(UnmanagedFunctionPointerAttribute)) is CustomAttribute att)
        {
            CustomAttributeValue<TypeSyntax> args = att.DecodeValue(CustomAttributeTypeProvider.Instance);
            callingConvention = (CallingConvention)(int)args.FixedArguments[0].Value!;
        }

        this.GetSignatureForDelegate(typeDef, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes);
        TypeSyntaxAndMarshaling returnValue = signature.ReturnType.ToTypeSyntax(typeSettings, returnTypeAttributes);

        DelegateDeclarationSyntax result = DelegateDeclaration(returnValue.Type, Identifier(name))
            .WithParameterList(FixTrivia(this.CreateParameterList(invokeMethodDef, signature, typeSettings)))
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword));
        result = returnValue.AddReturnMarshalAs(result);

        if (callingConvention.HasValue)
        {
            result = result.AddAttributeLists(AttributeList().AddAttributes(UnmanagedFunctionPointer(callingConvention.Value)).WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)));
        }

        return result;
    }

    private MemberDeclarationSyntax DeclareUntypedDelegate(TypeDefinition typeDef)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        IdentifierNameSyntax valueFieldName = IdentifierName("Value");

        // internal IntPtr Value;
        FieldDeclarationSyntax valueField = FieldDeclaration(VariableDeclaration(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)))
            .AddVariables(VariableDeclarator(valueFieldName.Identifier))).AddModifiers(TokenWithSpace(this.Visibility));

        // internal T CreateDelegate<T>() => Marshal.GetDelegateForFunctionPointer<T>(this.Value);
        IdentifierNameSyntax typeParameter = IdentifierName("TDelegate");
        MemberAccessExpressionSyntax methodToCall = this.getDelegateForFunctionPointerGenericExists
            ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), GenericName(nameof(Marshal.GetDelegateForFunctionPointer)).AddTypeArgumentListArguments(typeParameter))
            : MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Marshal)), IdentifierName(nameof(Marshal.GetDelegateForFunctionPointer)));
        ArgumentListSyntax arguments = ArgumentList().AddArguments(Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), valueFieldName)));
        if (!this.getDelegateForFunctionPointerGenericExists)
        {
            arguments = arguments.AddArguments(Argument(TypeOfExpression(typeParameter)));
        }

        ExpressionSyntax bodyExpression = InvocationExpression(methodToCall, arguments);
        if (!this.getDelegateForFunctionPointerGenericExists)
        {
            bodyExpression = CastExpression(typeParameter, bodyExpression);
        }

        MethodDeclarationSyntax createDelegateMethod = MethodDeclaration(typeParameter, Identifier("CreateDelegate"))
            .AddTypeParameterListParameters(TypeParameter(typeParameter.Identifier))
            .AddConstraintClauses(TypeParameterConstraintClause(typeParameter, SingletonSeparatedList<TypeParameterConstraintSyntax>(TypeConstraint(IdentifierName("Delegate")))))
            .WithExpressionBody(ArrowExpressionClause(bodyExpression))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithSemicolonToken(SemicolonWithLineFeed);

        StructDeclarationSyntax typedefStruct = StructDeclaration(name.Identifier)
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword)))
            .AddMembers(valueField)
            .AddMembers(this.CreateCommonTypeDefMembers(name, IntPtrTypeSyntax, valueFieldName).ToArray())
            .AddMembers(createDelegateMethod);
        return typedefStruct;
    }

    private void GetSignatureForDelegate(TypeDefinition typeDef, out MethodDefinition invokeMethodDef, out MethodSignature<TypeHandleInfo> signature, out CustomAttributeHandleCollection? returnTypeAttributes)
    {
        invokeMethodDef = typeDef.GetMethods().Select(this.Reader.GetMethodDefinition).Single(def => this.Reader.StringComparer.Equals(def.Name, "Invoke"));
        signature = invokeMethodDef.DecodeSignature(SignatureHandleProvider.Instance, null);
        returnTypeAttributes = this.GetReturnTypeCustomAttributes(invokeMethodDef);
    }

    private StructDeclarationSyntax DeclareStruct(TypeDefinitionHandle typeDefHandle, Context context)
    {
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        bool isManagedType = this.IsManagedType(typeDefHandle);
        IdentifierNameSyntax name = IdentifierName(this.GetMangledIdentifier(this.Reader.GetString(typeDef.Name), context.AllowMarshaling, isManagedType));
        bool explicitLayout = (typeDef.Attributes & TypeAttributes.ExplicitLayout) == TypeAttributes.ExplicitLayout;
        if (explicitLayout)
        {
            context = context with { AllowMarshaling = false };
        }

        TypeSyntaxSettings typeSettings = context.Filter(this.fieldTypeSettings);

        bool hasUtf16CharField = false;
        var members = new List<MemberDeclarationSyntax>();
        SyntaxList<MemberDeclarationSyntax> additionalMembers = default;
        foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            string fieldName = this.Reader.GetString(fieldDef.Name);

            try
            {
                CustomAttribute? fixedBufferAttribute = this.FindAttribute(fieldDef.GetCustomAttributes(), SystemRuntimeCompilerServices, nameof(FixedBufferAttribute));

                FieldDeclarationSyntax field;
                VariableDeclaratorSyntax fieldDeclarator = VariableDeclarator(SafeIdentifier(fieldName));
                if (fixedBufferAttribute.HasValue)
                {
                    CustomAttributeValue<TypeSyntax> attributeArgs = fixedBufferAttribute.Value.DecodeValue(CustomAttributeTypeProvider.Instance);
                    var fieldType = (TypeSyntax)attributeArgs.FixedArguments[0].Value!;
                    ExpressionSyntax size = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)attributeArgs.FixedArguments[1].Value!));
                    field = FieldDeclaration(
                        VariableDeclaration(fieldType))
                        .AddDeclarationVariables(
                            fieldDeclarator
                                .WithArgumentList(BracketedArgumentList(SingletonSeparatedList(Argument(size)))))
                        .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), Token(SyntaxKind.FixedKeyword));
                }
                else
                {
                    CustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes();
                    TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null);
                    hasUtf16CharField |= fieldTypeInfo is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Char };
                    TypeSyntaxAndMarshaling fieldTypeSyntax = fieldTypeInfo.ToTypeSyntax(typeSettings, fieldAttributes);
                    (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) fieldInfo = this.ReinterpretFieldType(fieldDef, fieldTypeSyntax.Type, fieldAttributes, context);
                    additionalMembers = additionalMembers.AddRange(fieldInfo.AdditionalMembers);

                    field = FieldDeclaration(VariableDeclaration(fieldInfo.FieldType).AddVariables(fieldDeclarator))
                        .AddModifiers(TokenWithSpace(this.Visibility));

                    if (fieldInfo.MarshalAsAttribute is object)
                    {
                        field = field.AddAttributeLists(AttributeList().AddAttributes(fieldInfo.MarshalAsAttribute));
                    }

                    if (this.HasObsoleteAttribute(fieldDef.GetCustomAttributes()))
                    {
                        field = field.AddAttributeLists(AttributeList().AddAttributes(ObsoleteAttributeSyntax));
                    }

                    if (fieldInfo.FieldType is PointerTypeSyntax || fieldInfo.FieldType is FunctionPointerTypeSyntax)
                    {
                        field = field.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                    }

                    if (ObjectMembers.Contains(fieldName))
                    {
                        field = field.AddModifiers(TokenWithSpace(SyntaxKind.NewKeyword));
                    }
                }

                int offset = fieldDef.GetOffset();
                if (offset >= 0)
                {
                    field = field.AddAttributeLists(AttributeList().AddAttributes(FieldOffset(offset)));
                }

                members.Add(field);
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException("Failed while generating field: " + fieldName, ex);
            }
        }

        // Add the additional members, taking care to not introduce redundant declarations.
        members.AddRange(additionalMembers.Where(c => c is not StructDeclarationSyntax cs || !members.OfType<StructDeclarationSyntax>().Any(m => m.Identifier.ValueText == cs.Identifier.ValueText)));

        switch (name.Identifier.ValueText)
        {
            case "RECT":
            case "SIZE":
            case "SYSTEMTIME":
            case "DECIMAL":
                members.AddRange(this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                break;
            default:
                break;
        }

        StructDeclarationSyntax result = StructDeclaration(name.Identifier)
            .AddMembers(members.ToArray())
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword)));

        TypeLayout layout = typeDef.GetLayout();
        CharSet charSet = hasUtf16CharField ? CharSet.Unicode : CharSet.Ansi;
        if (!layout.IsDefault || explicitLayout || charSet != CharSet.Ansi)
        {
            result = result.AddAttributeLists(AttributeList().AddAttributes(StructLayout(typeDef.Attributes, layout, charSet)));
        }

        if (this.FindGuidFromAttribute(typeDef) is Guid guid)
        {
            result = result.AddAttributeLists(AttributeList().AddAttributes(GUID(guid)));
        }

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);

        return result;
    }

    /// <summary>
    /// Creates an empty class that when instantiated, creates a cocreatable Windows object
    /// that may implement a number of interfaces at runtime, discoverable only by documentation.
    /// </summary>
    private ClassDeclarationSyntax DeclareCocreatableClass(TypeDefinition typeDef)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        Guid guid = this.FindGuidFromAttribute(typeDef) ?? throw new ArgumentException("Type does not have a GuidAttribute.");
        SyntaxTokenList classModifiers = TokenList(TokenWithSpace(this.Visibility));
        classModifiers = classModifiers.Add(TokenWithSpace(SyntaxKind.PartialKeyword));
        ClassDeclarationSyntax result = ClassDeclaration(name.Identifier)
            .WithModifiers(classModifiers)
            .AddAttributeLists(AttributeList().AddAttributes(GUID(guid), ComImportAttributeSyntax));

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);
        return result;
    }

    private bool IsHandle(EntityHandle typeDefOrRefHandle, out string? releaseMethodName)
    {
        switch (typeDefOrRefHandle.Kind)
        {
            case HandleKind.TypeReference when this.TryGetTypeDefHandle((TypeReferenceHandle)typeDefOrRefHandle, out TypeDefinitionHandle typeDefHandle):
                return this.IsHandle(typeDefHandle, out releaseMethodName);
            case HandleKind.TypeDefinition:
                return this.IsHandle((TypeDefinitionHandle)typeDefOrRefHandle, out releaseMethodName);
        }

        releaseMethodName = null;
        return false;
    }

    private bool IsHandle(TypeDefinitionHandle typeDefHandle, out string? releaseMethodName)
    {
        if (this.MetadataIndex.HandleTypeReleaseMethod.TryGetValue(typeDefHandle, out releaseMethodName))
        {
            return true;
        }

        // Special case handles that do not carry RAIIFree attributes.
        releaseMethodName = null;
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        return this.Reader.StringComparer.Equals(typeDef.Name, "HGDIOBJ")
            || this.Reader.StringComparer.Equals(typeDef.Name, "HWND");
    }

    private bool IsSafeHandleCompatibleTypeDef(TypeHandleInfo? typeDef)
    {
        return this.TryGetTypeDefFieldType(typeDef, out TypeHandleInfo? fieldType) && this.IsSafeHandleCompatibleTypeDefFieldType(fieldType);
    }

    private bool IsSafeHandleCompatibleTypeDefFieldType(TypeHandleInfo? fieldType)
    {
        return fieldType is PointerTypeHandleInfo
            || fieldType is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr };
    }

    /// <summary>
    /// Creates a struct that emulates a typedef in the C language headers.
    /// </summary>
    private StructDeclarationSyntax DeclareTypeDefStruct(TypeDefinition typeDef, TypeDefinitionHandle typeDefHandle)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        bool isHandle = this.IsHandle(typeDefHandle, out string? freeMethodName);
        if (freeMethodName is not null)
        {
            this.TryGenerateExternMethod(freeMethodName, out _);
        }

        TypeSyntaxSettings typeSettings = isHandle ? this.fieldOfHandleTypeDefTypeSettings : this.fieldTypeSettings;

        FieldDefinition fieldDef = this.Reader.GetFieldDefinition(typeDef.GetFields().Single());
        string fieldName = this.Reader.GetString(fieldDef.Name);
        IdentifierNameSyntax fieldIdentifierName = SafeIdentifierName(fieldName);
        VariableDeclaratorSyntax fieldDeclarator = VariableDeclarator(fieldIdentifierName.Identifier);
        CustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes();
        TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null);
        TypeSyntaxAndMarshaling fieldType = fieldTypeInfo.ToTypeSyntax(typeSettings, fieldAttributes);
        (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? _) fieldInfo =
            this.ReinterpretFieldType(fieldDef, fieldType.Type, fieldAttributes, this.DefaultContext);
        SyntaxList<MemberDeclarationSyntax> members = List<MemberDeclarationSyntax>();

        FieldDeclarationSyntax fieldSyntax = FieldDeclaration(
            VariableDeclaration(fieldInfo.FieldType).AddVariables(fieldDeclarator))
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword));
        members = members.Add(fieldSyntax);

        members = members.AddRange(this.CreateCommonTypeDefMembers(name, fieldInfo.FieldType, fieldIdentifierName));

        IdentifierNameSyntax valueParameter = IdentifierName("value");
        MemberAccessExpressionSyntax fieldAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), fieldIdentifierName);

        if (isHandle && this.IsSafeHandleCompatibleTypeDefFieldType(fieldTypeInfo) && fieldTypeInfo is not PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr })
        {
            // Handle types must interop with IntPtr for SafeHandle support, so if IntPtr isn't the field type,
            // we need to create new conversion operators.
            ExpressionSyntax valueValueArg = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldIdentifierName);
            if (fieldTypeInfo is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.UIntPtr })
            {
                valueValueArg = CastExpression(PredefinedType(TokenWithSpace(SyntaxKind.ULongKeyword)), valueValueArg);

                // We still need to make conversion from an IntPtr simple since so much code relies on it.
                // public static explicit operator SOCKET(IntPtr value) => new SOCKET((UIntPtr)unchecked((ulong)value.ToInt64()));
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(name).AddArgumentListArguments(
                        Argument(CastExpression(fieldInfo.FieldType, UncheckedExpression(CastExpression(PredefinedType(Token(SyntaxKind.ULongKeyword)), InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToInt64)))))))))))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }
            else
            {
                // public static implicit operator IntPtr(MSIHANDLE value) => new IntPtr(value.Value);
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), IntPtrTypeSyntax)
                    .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(name.WithTrailingTrivia(TriviaList(Space))))
                    .WithExpressionBody(ArrowExpressionClause(
                        ObjectCreationExpression(IntPtrTypeSyntax).AddArgumentListArguments(Argument(valueValueArg))))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }

            if (fieldInfo.FieldType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.UIntKeyword } })
            {
                // public static explicit operator MSIHANDLE(IntPtr value) => new MSIHANDLE((uint)value.ToInt32());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(name).AddArgumentListArguments(
                        Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToInt32)))))))))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }

            if (fieldInfo.FieldType is PointerTypeSyntax)
            {
                // public static explicit operator MSIHANDLE(IntPtr value) => new MSIHANDLE(value.ToPointer());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(name).AddArgumentListArguments(
                        Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer)))))))))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));

                // public static explicit operator MSIHANDLE(UIntPtr value) => new MSIHANDLE(value.ToPointer());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(UIntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(name).AddArgumentListArguments(
                        Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer)))))))))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }
        }

        switch (name.Identifier.ValueText)
        {
            case "PWSTR":
            case "PSTR":
                members = members.AddRange(this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                this.TryGenerateType("Windows.Win32.Foundation.PC" + name.Identifier.ValueText.Substring(1)); // the template references its constant version
                break;
            case "BSTR":
            case "HRESULT":
            case "NTSTATUS":
            case "BOOL":
            case "BOOLEAN":
                members = members.AddRange(this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                break;
            default:
                break;
        }

        SyntaxTokenList structModifiers = TokenList(TokenWithSpace(this.Visibility));
        if (RequiresUnsafe(fieldInfo.FieldType))
        {
            structModifiers = structModifiers.Add(TokenWithSpace(SyntaxKind.UnsafeKeyword));
        }

        structModifiers = structModifiers.Add(TokenWithSpace(SyntaxKind.ReadOnlyKeyword)).Add(TokenWithSpace(SyntaxKind.PartialKeyword));
        StructDeclarationSyntax result = StructDeclaration(name.Identifier)
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(GenericName(nameof(IEquatable<int>), TypeArgumentList().WithGreaterThanToken(TokenWithLineFeed(SyntaxKind.GreaterThanToken))).AddTypeArgumentListArguments(name)))).WithColonToken(TokenWithSpace(SyntaxKind.ColonToken)))
            .WithMembers(members)
            .WithModifiers(structModifiers)
            .AddAttributeLists(AttributeList().WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)).AddAttributes(DebuggerDisplay("{" + fieldName + "}")));

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);
        return result;
    }

    private IEnumerable<MemberDeclarationSyntax> CreateCommonTypeDefMembers(IdentifierNameSyntax structName, TypeSyntax fieldType, IdentifierNameSyntax fieldName)
    {
        // Add constructor
        IdentifierNameSyntax valueParameter = IdentifierName("value");
        MemberAccessExpressionSyntax fieldAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), fieldName);
        yield return ConstructorDeclaration(structName.Identifier)
            .AddModifiers(TokenWithSpace(this.Visibility))
            .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(fieldType.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, fieldAccessExpression, valueParameter).WithOperatorToken(TokenWithSpaces(SyntaxKind.EqualsToken))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // If this typedef struct represents a pointer, add an IsNull property.
        if (fieldType is IdentifierNameSyntax { Identifier: { Value: nameof(IntPtr) or nameof(UIntPtr) } })
        {
            // internal static HWND Null => default;
            yield return PropertyDeclaration(structName.WithTrailingTrivia(TriviaList(Space)), "Null")
                .WithExpressionBody(ArrowExpressionClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression)))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .WithSemicolonToken(SemicolonWithLineFeed);

            // internal static bool IsNull => value == default;
            yield return PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), "IsNull")
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithExpressionBody(ArrowExpressionClause(BinaryExpression(SyntaxKind.EqualsExpression, fieldName, LiteralExpression(SyntaxKind.DefaultLiteralExpression))))
                .WithSemicolonToken(SemicolonWithLineFeed);
        }

        // public static implicit operator int(HWND value) => value.Value;
        yield return ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fieldType)
            .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldName)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static explicit operator HWND(int value) => new HWND(value);
        // Except make converting char* or byte* to typedefs representing strings, and LPARAM/WPARAM to nint/nuint, implicit.
        SyntaxKind explicitOrImplicitModifier = ImplicitConversionTypeDefs.Contains(structName.Identifier.ValueText) ? SyntaxKind.ImplicitKeyword : SyntaxKind.ExplicitKeyword;
        yield return ConversionOperatorDeclaration(Token(explicitOrImplicitModifier), structName)
            .AddParameterListParameters(Parameter(valueParameter.Identifier).WithType(fieldType.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(structName).AddArgumentListArguments(Argument(valueParameter))))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static bool operator ==(HANDLE left, HANDLE right) => left.Value == right.Value;
        IdentifierNameSyntax? leftParameter = IdentifierName("left");
        IdentifierNameSyntax? rightParameter = IdentifierName("right");
        yield return OperatorDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), TokenWithNoSpace(SyntaxKind.EqualsEqualsToken))
            .WithOperatorKeyword(TokenWithSpace(SyntaxKind.OperatorKeyword))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(
                Parameter(leftParameter.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))),
                Parameter(rightParameter.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, leftParameter, fieldName),
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, rightParameter, fieldName))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static bool operator !=(HANDLE left, HANDLE right) => !(left == right);
        yield return OperatorDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Token(SyntaxKind.ExclamationEqualsToken))
            .WithOperatorKeyword(TokenWithSpace(SyntaxKind.OperatorKeyword))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(
                Parameter(leftParameter.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))),
                Parameter(rightParameter.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(
                PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    ParenthesizedExpression(BinaryExpression(SyntaxKind.EqualsExpression, leftParameter, rightParameter)))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public bool Equals(HWND other) => this.Value == other.Value;
        IdentifierNameSyntax other = IdentifierName("other");
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier(nameof(IEquatable<int>.Equals)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(Parameter(other.Identifier).WithType(structName.WithTrailingTrivia(TriviaList(Space))))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    fieldAccessExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, other, fieldName))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public override bool Equals(object obj) => obj is HWND other && this.Equals(other);
        IdentifierNameSyntax objParam = IdentifierName("obj");
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier(nameof(IEquatable<int>.Equals)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
            .AddParameterListParameters(Parameter(objParam.Identifier).WithType(PredefinedType(TokenWithSpace(SyntaxKind.ObjectKeyword))))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    IsPatternExpression(objParam, DeclarationPattern(structName, SingleVariableDesignation(Identifier("other")))),
                    InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(nameof(Equals))))
                        .WithArgumentList(ArgumentList().AddArguments(Argument(IdentifierName("other")))))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public override int GetHashCode() => unchecked((int)this.Value); // if Value is a pointer
        // public override int GetHashCode() => this.Value.GetHashCode(); // if Value is not a pointer
        ExpressionSyntax hashExpr = fieldType is PointerTypeSyntax ?
            UncheckedExpression(CastExpression(PredefinedType(TokenWithNoSpace(SyntaxKind.IntKeyword)), fieldAccessExpression)) :
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, fieldAccessExpression, IdentifierName(nameof(object.GetHashCode))),
                ArgumentList());
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), Identifier(nameof(object.GetHashCode)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
            .WithExpressionBody(ArrowExpressionClause(hashExpr))
            .WithSemicolonToken(SemicolonWithLineFeed);
    }

    private IEnumerable<MemberDeclarationSyntax> ExtractMembersFromTemplate(string name) => ((TypeDeclarationSyntax)this.FetchTemplate($"{name}")).Members;

    /// <summary>
    /// Promotes an <see langword="internal" /> member to be <see langword="public"/> if <see cref="Visibility"/> indicates that generated APIs should be public.
    /// This change is applied recursively.
    /// </summary>
    /// <param name="member">The member to potentially make public.</param>
    /// <returns>The modified or original <paramref name="member"/>.</returns>
    private MemberDeclarationSyntax ElevateVisibility(MemberDeclarationSyntax member)
    {
        if (this.Visibility == SyntaxKind.PublicKeyword)
        {
            MemberDeclarationSyntax publicMember = member;
            int indexOfInternal = publicMember.Modifiers.IndexOf(SyntaxKind.InternalKeyword);
            if (indexOfInternal >= 0)
            {
                publicMember = publicMember.WithModifiers(publicMember.Modifiers.Replace(publicMember.Modifiers[indexOfInternal], TokenWithSpace(this.Visibility)));
            }

            // Apply change recursively.
            if (publicMember is TypeDeclarationSyntax memberContainer)
            {
                publicMember = memberContainer.WithMembers(List(memberContainer.Members.Select(this.ElevateVisibility)));
            }

            return publicMember;
        }

        return member;
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

    private EnumDeclarationSyntax DeclareEnum(TypeDefinition typeDef)
    {
        bool flagsEnum = this.FindAttribute(typeDef.GetCustomAttributes(), nameof(System), nameof(FlagsAttribute)) is not null;

        var enumValues = new List<SyntaxNodeOrToken>();
        TypeSyntax? enumBaseType = null;
        foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            string enumValueName = this.Reader.GetString(fieldDef.Name);
            ConstantHandle valueHandle = fieldDef.GetDefaultValue();
            if (valueHandle.IsNil)
            {
                enumBaseType = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null).ToTypeSyntax(this.enumTypeSettings, null).Type;
                continue;
            }

            bool enumBaseTypeIsSigned = enumBaseType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.LongKeyword or (int)SyntaxKind.IntKeyword or (int)SyntaxKind.ShortKeyword or (int)SyntaxKind.SByteKeyword } };
            Constant value = this.Reader.GetConstant(valueHandle);
            ExpressionSyntax enumValue = flagsEnum ? this.ToHexExpressionSyntax(value, enumBaseTypeIsSigned) : this.ToExpressionSyntax(value);
            EnumMemberDeclarationSyntax enumMember = EnumMemberDeclaration(SafeIdentifier(enumValueName))
                .WithEqualsValue(EqualsValueClause(enumValue));
            enumValues.Add(enumMember);
            enumValues.Add(TokenWithLineFeed(SyntaxKind.CommaToken));
        }

        if (enumBaseType is null)
        {
            throw new NotSupportedException("Unknown enum type.");
        }

        string? name = this.Reader.GetString(typeDef.Name);
        EnumDeclarationSyntax result = EnumDeclaration(Identifier(name))
            .WithMembers(SeparatedList<EnumMemberDeclarationSyntax>(enumValues))
            .WithModifiers(TokenList(TokenWithSpace(this.Visibility)));

        if (!(enumBaseType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } }))
        {
            result = result.WithIdentifier(result.Identifier.WithTrailingTrivia(Space))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(enumBaseType).WithTrailingTrivia(LineFeed))).WithColonToken(TokenWithSpace(SyntaxKind.ColonToken)));
        }

        if (flagsEnum)
        {
            result = result.AddAttributeLists(
                AttributeList().WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)).AddAttributes(FlagsAttributeSyntax));
        }

        result = this.AddApiDocumentation(name, result);

        return result;
    }

    private IEnumerable<MethodDeclarationSyntax> DeclareFriendlyOverloads(MethodDefinition methodDefinition, MethodDeclarationSyntax externMethodDeclaration, NameSyntax declaringTypeName, FriendlyOverloadOf overloadOf, HashSet<string> helperMethodsAdded)
    {
        // If/when we ever need helper methods for the friendly overloads again, they can be added when used with code like this:
        ////if (helperMethodsAdded.Add(SomeHelperMethodName))
        ////{
        ////    yield return PInvokeHelperMethods[SomeHelperMethodName];
        ////}

        if (this.TryFetchTemplate(externMethodDeclaration.Identifier.ValueText, out MemberDeclarationSyntax? templateFriendlyOverload))
        {
            yield return (MethodDeclarationSyntax)templateFriendlyOverload;
        }

        if (externMethodDeclaration.Identifier.ValueText != "CoCreateInstance" || !this.options.ComInterop.UseIntPtrForComOutPointers)
        {
            if (this.options.AllowMarshaling && this.TryFetchTemplate("marshaling/" + externMethodDeclaration.Identifier.ValueText, out templateFriendlyOverload))
            {
                yield return (MethodDeclarationSyntax)templateFriendlyOverload;
            }

            if (!this.options.AllowMarshaling && this.TryFetchTemplate("no_marshaling/" + externMethodDeclaration.Identifier.ValueText, out templateFriendlyOverload))
            {
                yield return (MethodDeclarationSyntax)templateFriendlyOverload;
            }
        }

#pragma warning disable SA1114 // Parameter list should follow declaration
        static ParameterSyntax StripAttributes(ParameterSyntax parameter) => parameter.WithAttributeLists(List<AttributeListSyntax>());
        static ExpressionSyntax GetSpanLength(ExpressionSyntax span) => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<int>.Length)));
        bool isReleaseMethod = this.MetadataIndex.ReleaseMethods.Contains(externMethodDeclaration.Identifier.ValueText);
        bool doNotRelease = this.FindInteropDecorativeAttribute(this.GetReturnTypeCustomAttributes(methodDefinition), DoNotReleaseAttribute) is not null;

        TypeSyntaxSettings parameterTypeSyntaxSettings = overloadOf switch
        {
            FriendlyOverloadOf.ExternMethod => this.externSignatureTypeSettings,
            FriendlyOverloadOf.StructMethod => this.extensionMethodSignatureTypeSettings,
            FriendlyOverloadOf.InterfaceMethod => this.extensionMethodSignatureTypeSettings,
            _ => throw new NotSupportedException(overloadOf.ToString()),
        };

        MethodSignature<TypeHandleInfo> originalSignature = methodDefinition.DecodeSignature(SignatureHandleProvider.Instance, null);
        var parameters = externMethodDeclaration.ParameterList.Parameters.Select(StripAttributes).ToList();
        var lengthParamUsedBy = new Dictionary<int, int>();
        var arguments = externMethodDeclaration.ParameterList.Parameters.Select(p => Argument(IdentifierName(p.Identifier.Text)).WithRefKindKeyword(p.Modifiers.FirstOrDefault(p => p.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword))).ToList();
        TypeSyntax? externMethodReturnType = externMethodDeclaration.ReturnType.WithoutLeadingTrivia();
        var fixedBlocks = new List<VariableDeclarationSyntax>();
        var leadingOutsideTryStatements = new List<StatementSyntax>();
        var leadingStatements = new List<StatementSyntax>();
        var trailingStatements = new List<StatementSyntax>();
        var finallyStatements = new List<StatementSyntax>();
        bool signatureChanged = false;
        foreach (ParameterHandle paramHandle in methodDefinition.GetParameters())
        {
            Parameter param = this.Reader.GetParameter(paramHandle);
            if (param.SequenceNumber == 0 || param.SequenceNumber - 1 >= parameters.Count)
            {
                continue;
            }

            bool isOptional = (param.Attributes & ParameterAttributes.Optional) == ParameterAttributes.Optional;
            bool isIn = (param.Attributes & ParameterAttributes.In) == ParameterAttributes.In;
            bool isConst = this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), "ConstAttribute") is not null;
            bool isComOutPtr = this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), "ComOutPtrAttribute") is not null;
            bool isOut = isComOutPtr || (param.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;

            // TODO:
            // * Review double/triple pointer scenarios.
            //   * Consider CredEnumerateA, which is a "pointer to an array of pointers" (3-asterisks!). How does FriendlyAttribute improve this, if at all? The memory must be freed through another p/invoke.
            ParameterSyntax externParam = parameters[param.SequenceNumber - 1];
            if (externParam.Type is null)
            {
                throw new GenerationFailedException();
            }

            TypeHandleInfo parameterTypeInfo = originalSignature.ParameterTypes[param.SequenceNumber - 1];
            bool isManagedParameterType = this.IsManagedType(parameterTypeInfo);
            IdentifierNameSyntax origName = IdentifierName(externParam.Identifier.ValueText);

            if (isManagedParameterType && (externParam.Modifiers.Any(SyntaxKind.OutKeyword) || externParam.Modifiers.Any(SyntaxKind.RefKeyword)))
            {
                bool hasOut = externParam.Modifiers.Any(SyntaxKind.OutKeyword);
                arguments[param.SequenceNumber - 1] = arguments[param.SequenceNumber - 1].WithRefKindKeyword(TokenWithSpace(hasOut ? SyntaxKind.OutKeyword : SyntaxKind.RefKeyword));
            }
            else if (isOut && !isIn && !isReleaseMethod && parameterTypeInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo pointedElementInfo } && this.TryGetHandleReleaseMethod(pointedElementInfo.Handle, out string? outReleaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, outReleaseMethod))
            {
                if (this.RequestSafeHandle(outReleaseMethod) is TypeSyntax safeHandleType)
                {
                    signatureChanged = true;

                    IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");

                    // out SafeHandle
                    parameters[param.SequenceNumber - 1] = externParam
                        .WithType(safeHandleType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.OutKeyword)));

                    // HANDLE SomeLocal;
                    leadingStatements.Add(LocalDeclarationStatement(VariableDeclaration(pointedElementInfo.ToTypeSyntax(parameterTypeSyntaxSettings, null).Type).AddVariables(
                        VariableDeclarator(typeDefHandleName.Identifier))));

                    // Argument: &SomeLocal
                    arguments[param.SequenceNumber - 1] = Argument(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, typeDefHandleName));

                    // Some = new SafeHandle(SomeLocal, ownsHandle: true);
                    trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        origName,
                        ObjectCreationExpression(safeHandleType).AddArgumentListArguments(
                            Argument(GetIntPtrFromTypeDef(typeDefHandleName, pointedElementInfo)),
                            Argument(LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)).WithNameColon(NameColon(IdentifierName("ownsHandle")))))));
                }
            }
            else if (this.options.UseSafeHandles && isIn && !isOut && !isReleaseMethod && parameterTypeInfo is HandleTypeHandleInfo parameterHandleTypeInfo && this.TryGetHandleReleaseMethod(parameterHandleTypeInfo.Handle, out string? releaseMethod) && !this.Reader.StringComparer.Equals(methodDefinition.Name, releaseMethod)
                && !(this.TryGetTypeDefFieldType(parameterHandleTypeInfo, out TypeHandleInfo? fieldType) && !this.IsSafeHandleCompatibleTypeDefFieldType(fieldType)))
            {
                IdentifierNameSyntax typeDefHandleName = IdentifierName(externParam.Identifier.ValueText + "Local");
                signatureChanged = true;

                IdentifierNameSyntax refAddedName = IdentifierName(externParam.Identifier.ValueText + "AddRef");

                // bool hParamNameAddRef = false;
                leadingOutsideTryStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword))).AddVariables(
                        VariableDeclarator(refAddedName.Identifier).WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.FalseLiteralExpression))))));

                // HANDLE hTemplateFileLocal;
                leadingStatements.Add(LocalDeclarationStatement(VariableDeclaration(externParam.Type).AddVariables(
                    VariableDeclarator(typeDefHandleName.Identifier))));

                // throw new ArgumentNullException(nameof(hTemplateFile));
                StatementSyntax nullHandleStatement = ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentNullException))).WithArgumentList(ArgumentList().AddArguments(Argument(NameOfExpression(IdentifierName(externParam.Identifier.ValueText))))));
                if (isOptional)
                {
                    HashSet<IntPtr> invalidValues = this.GetInvalidHandleValues(parameterHandleTypeInfo.Handle);
                    if (invalidValues.Count > 0)
                    {
                        // (HANDLE)new IntPtr(-1);
                        IntPtr invalidValue = GetPreferredInvalidHandleValue(invalidValues);
                        ExpressionSyntax invalidExpression = CastExpression(externParam.Type, IntPtrExpr(invalidValue));

                        // hTemplateFileLocal = invalid-handle-value;
                        nullHandleStatement = ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, typeDefHandleName, invalidExpression));
                    }
                }

                // if (hTemplateFile is object)
                leadingStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                    Block().AddStatements(
                    //// hTemplateFile.DangerousAddRef(ref hTemplateFileAddRef);
                    ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousAddRef))))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(refAddedName).WithRefKindKeyword(TokenWithSpace(SyntaxKind.RefKeyword)))))),
                    //// hTemplateFileLocal = (HANDLE)hTemplateFile.DangerousGetHandle();
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            typeDefHandleName,
                            CastExpression(
                                externParam.Type.WithoutTrailingTrivia(),
                                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousGetHandle))), ArgumentList())))
                        .WithOperatorToken(TokenWithSpaces(SyntaxKind.EqualsToken)))),
                    //// else hTemplateFileLocal = default;
                    ElseClause(nullHandleStatement)));

                // if (hTemplateFileAddRef)
                //     hTemplateFile.DangerousRelease();
                finallyStatements.Add(
                    IfStatement(
                        refAddedName,
                        ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(SafeHandle.DangerousRelease))), ArgumentList())))
                    .WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken)));

                // Accept the SafeHandle instead.
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(IdentifierName(nameof(SafeHandle)).WithTrailingTrivia(TriviaList(Space)));

                // hParamNameLocal;
                arguments[param.SequenceNumber - 1] = Argument(typeDefHandleName);
            }
            else if ((externParam.Type is PointerTypeSyntax { ElementType: TypeSyntax ptrElementType }
                && !IsVoid(ptrElementType)
                && !this.IsInterface(parameterTypeInfo)
                && this.canUseSpan) ||
                externParam.Type is ArrayTypeSyntax)
            {
                TypeSyntax elementType = externParam.Type is PointerTypeSyntax ptr ? ptr.ElementType
                    : externParam.Type is ArrayTypeSyntax array ? array.ElementType
                    : throw new InvalidOperationException();
                bool isPointerToPointer = elementType is PointerTypeSyntax or FunctionPointerTypeSyntax;

                // If there are no SAL annotations at all...
                if (!isOptional && !isIn && !isOut)
                {
                    // Consider that const means [In]
                    if (isConst)
                    {
                        isIn = true;
                        isOut = false;
                    }
                    else
                    {
                        // Otherwise assume bidirectional.
                        isIn = isOut = true;
                    }
                }

                bool isArray = false;
                bool isNullTerminated = false; // TODO
                short? sizeParamIndex = null;
                int? sizeConst = null;
                if (this.FindInteropDecorativeAttribute(param.GetCustomAttributes(), NativeArrayInfoAttribute) is CustomAttribute att)
                {
                    isArray = true;
                    NativeArrayInfo nativeArrayInfo = DecodeNativeArrayInfoAttribute(att);
                    sizeParamIndex = nativeArrayInfo.CountParamIndex;
                    sizeConst = nativeArrayInfo.CountConst;
                }

                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                if (isArray)
                {
                    // TODO: add support for in/out size parameters. (e.g. RSGetViewports)
                    // TODO: add support for lists of pointers via a generated pointer-wrapping struct (e.g. PSSetSamplers)

                    // It is possible that sizeParamIndex points to a parameter that is not on the extern method
                    // when the parameter is the last one and was moved to a return value.
                    if (sizeParamIndex.HasValue
                        && externMethodDeclaration.ParameterList.Parameters.Count > sizeParamIndex.Value
                        && !(externMethodDeclaration.ParameterList.Parameters[sizeParamIndex.Value].Type is PointerTypeSyntax)
                        && !isPointerToPointer)
                    {
                        signatureChanged = true;

                        if (lengthParamUsedBy.TryGetValue(sizeParamIndex.Value, out int userIndex))
                        {
                            // Multiple array parameters share a common 'length' parameter.
                            // Since we're making this a little less obvious, add a quick if check in the helper method
                            // that enforces that all such parameters have a common span length.
                            ExpressionSyntax otherUserName = IdentifierName(parameters[userIndex].Identifier.ValueText);
                            leadingStatements.Add(IfStatement(
                                BinaryExpression(
                                    SyntaxKind.NotEqualsExpression,
                                    GetSpanLength(otherUserName),
                                    GetSpanLength(origName)),
                                ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).WithArgumentList(ArgumentList()))));
                        }
                        else
                        {
                            lengthParamUsedBy.Add(sizeParamIndex.Value, param.SequenceNumber - 1);
                        }

                        if (externParam.Type is PointerTypeSyntax)
                        {
                            parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                                .WithType((isIn && isConst ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                            fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                                VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                            arguments[param.SequenceNumber - 1] = Argument(localName);
                        }

                        ExpressionSyntax sizeArgExpression = GetSpanLength(origName);
                        if (!(parameters[sizeParamIndex.Value].Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } }))
                        {
                            sizeArgExpression = CastExpression(parameters[sizeParamIndex.Value].Type!, sizeArgExpression);
                        }

                        arguments[sizeParamIndex.Value] = Argument(sizeArgExpression);
                    }
                    else if (sizeConst.HasValue && !isPointerToPointer && this.canUseSpan)
                    {
                        // TODO: add support for lists of pointers via a generated pointer-wrapping struct
                        signatureChanged = true;

                        // Accept a span instead of a pointer.
                        parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                            .WithType((isIn && isConst ? MakeReadOnlySpanOfT(elementType) : MakeSpanOfT(elementType)).WithTrailingTrivia(TriviaList(Space)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                            VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                        arguments[param.SequenceNumber - 1] = Argument(localName);

                        // Add a runtime check that the span is at least the required length.
                        leadingStatements.Add(IfStatement(
                            BinaryExpression(
                                SyntaxKind.LessThanExpression,
                                GetSpanLength(origName),
                                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(sizeConst.Value))),
                            ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).WithArgumentList(ArgumentList()))));
                    }
                    else if (isNullTerminated && isConst && parameters[param.SequenceNumber - 1].Type is PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.CharKeyword } } })
                    {
                        // replace char* with string
                        signatureChanged = true;
                        parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                            .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                        fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                            VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                        arguments[param.SequenceNumber - 1] = Argument(localName);
                    }
                }
                else if (isIn && isOptional && !isOut && !isPointerToPointer)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(NullableType(elementType).WithTrailingTrivia(TriviaList(Space)));
                    leadingStatements.Add(
                        LocalDeclarationStatement(VariableDeclaration(elementType)
                            .AddVariables(VariableDeclarator(localName.Identifier).WithInitializer(
                                EqualsValueClause(ConditionalExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("Value")),
                                    DefaultExpression(elementType)))))));
                    arguments[param.SequenceNumber - 1] = Argument(ConditionalExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                        PrefixUnaryExpression(SyntaxKind.AddressOfExpression, localName),
                        LiteralExpression(SyntaxKind.NullLiteralExpression)));
                }
                else if (isIn && isOut && !isOptional)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.RefKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
                else if (isOut && !isIn && !isOptional)
                {
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.OutKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
                else if (isIn && !isOut && !isOptional)
                {
                    // Use the "in" modifier to avoid copying the struct.
                    signatureChanged = true;
                    parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                        .WithType(elementType.WithTrailingTrivia(TriviaList(Space)))
                        .WithModifiers(TokenList(TokenWithSpace(SyntaxKind.InKeyword)));
                    fixedBlocks.Add(VariableDeclaration(externParam.Type).AddVariables(
                        VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                            PrefixUnaryExpression(SyntaxKind.AddressOfExpression, origName)))));
                    arguments[param.SequenceNumber - 1] = Argument(localName);
                }
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(origName))));
                arguments[param.SequenceNumber - 1] = Argument(localName);
            }
            else if (isIn && !isOut && isConst && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PCSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)));

                // fixed (byte* someLocal = some is object ? System.Text.Encoding.Default.GetBytes(some) : null)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.ByteKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                        ConditionalExpression(
                            BinaryExpression(SyntaxKind.IsExpression, origName, PredefinedType(Token(SyntaxKind.ObjectKeyword))),
                            InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseTypeName("global::System.Text.Encoding.Default"),
                                    IdentifierName(nameof(Encoding.GetBytes))))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(Argument(origName)))),
                            LiteralExpression(SyntaxKind.NullLiteralExpression))))));

                // new PCSTR(someLocal)
                arguments[param.SequenceNumber - 1] = Argument(ObjectCreationExpression(externParam.Type).AddArgumentListArguments(Argument(localName)));
            }
            else if (isIn && isOut && this.canUseSpan && externParam.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "PWSTR" } } })
            {
                IdentifierNameSyntax localName = IdentifierName("p" + origName);
                IdentifierNameSyntax localWstrName = IdentifierName("wstr" + origName);
                signatureChanged = true;
                parameters[param.SequenceNumber - 1] = externParam
                    .WithType(MakeSpanOfT(PredefinedType(Token(SyntaxKind.CharKeyword))))
                    .AddModifiers(Token(SyntaxKind.RefKeyword));

                // fixed (char* pParam1 = Param1)
                fixedBlocks.Add(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                    VariableDeclarator(localName.Identifier).WithInitializer(EqualsValueClause(
                        origName))));

                // wstrParam1
                arguments[param.SequenceNumber - 1] = Argument(localWstrName);

                // if (buffer.LastIndexOf('\0') == -1) throw new ArgumentException("Required null terminator is missing.", "Param1");
                InvocationExpressionSyntax lastIndexOf = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(MemoryExtensions.LastIndexOf))),
                    ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))));
                leadingOutsideTryStatements.Add(IfStatement(
                    BinaryExpression(SyntaxKind.EqualsExpression, lastIndexOf, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(-1))),
                    ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).AddArgumentListArguments(
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Required null terminator missing."))),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(externParam.Identifier.ValueText)))))));

                // PWSTR wstrParam1 = pParam1;
                leadingStatements.Add(LocalDeclarationStatement(
                    VariableDeclaration(externParam.Type).AddVariables(VariableDeclarator(localWstrName.Identifier).WithInitializer(EqualsValueClause(localName)))));

                // Param1 = Param1.Slice(0, wstrParam1.Length);
                trailingStatements.Add(ExpressionStatement(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    origName,
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName(nameof(Span<char>.Slice))),
                        ArgumentList().AddArguments(
                            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                            Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, localWstrName, IdentifierName("Length"))))))));
            }
            else if (isIn && isOptional && !isOut && isManagedParameterType && parameterTypeInfo is PointerTypeHandleInfo ptrInfo && ptrInfo.ElementType.IsValueType(parameterTypeSyntaxSettings) is true && this.canUseUnsafeAsRef)
            {
                // The extern method couldn't have exposed the parameter as a pointer because the type is managed.
                // It would have exposed as an `in` modifier, and non-optional. But we can expose as optional anyway.
                signatureChanged = true;
                IdentifierNameSyntax localName = IdentifierName(origName + "Local");
                parameters[param.SequenceNumber - 1] = parameters[param.SequenceNumber - 1]
                    .WithType(NullableType(externParam.Type).WithTrailingTrivia(TriviaList(Space)))
                    .WithModifiers(TokenList()); // drop the `in` modifier.
                leadingStatements.Add(
                    LocalDeclarationStatement(VariableDeclaration(externParam.Type)
                        .AddVariables(VariableDeclarator(localName.Identifier).WithInitializer(
                            EqualsValueClause(ConditionalExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("Value")),
                                DefaultExpression(externParam.Type)))))));

                // We can't pass in null, but we can be fancy to achieve the same effect.
                // Unsafe.NullRef<TParamType>() or Unsafe.AsRef<TParamType>(null), depending on what's available.
                ExpressionSyntax nullRef = this.canUseUnsafeNullRef
                    ? InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName("NullRef", TypeArgumentList().AddArguments(externParam.Type))),
                        ArgumentList())
                    : InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), GenericName(nameof(Unsafe.AsRef), TypeArgumentList().AddArguments(externParam.Type))),
                        ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))));
                arguments[param.SequenceNumber - 1] = Argument(ConditionalExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, origName, IdentifierName("HasValue")),
                    localName,
                    nullRef));
            }
        }

        TypeSyntax? returnSafeHandleType = originalSignature.ReturnType is HandleTypeHandleInfo returnTypeHandleInfo
            && this.TryGetHandleReleaseMethod(returnTypeHandleInfo.Handle, out string? returnReleaseMethod)
            ? this.RequestSafeHandle(returnReleaseMethod) : null;
        SyntaxToken friendlyMethodName = externMethodDeclaration.Identifier;

        if (returnSafeHandleType is object && !signatureChanged)
        {
            // The parameter types are all the same, but we need a friendly overload with a different return type.
            // Our only choice is to rename the friendly overload.
            friendlyMethodName = Identifier(externMethodDeclaration.Identifier.ValueText + "_SafeHandle");
            signatureChanged = true;
        }

        if (signatureChanged)
        {
            if (lengthParamUsedBy.Count > 0)
            {
                // Remove in reverse order so as to not invalidate the indexes of elements to remove.
                // Also take care to only remove each element once, even if it shows up multiple times in the collection.
                var parameterIndexesToRemove = new SortedSet<int>(lengthParamUsedBy.Keys);
                foreach (int indexToRemove in parameterIndexesToRemove.Reverse())
                {
                    parameters.RemoveAt(indexToRemove);
                }
            }

            TypeSyntax docRefExternName = overloadOf == FriendlyOverloadOf.InterfaceMethod
                ? QualifiedName(declaringTypeName, IdentifierName(externMethodDeclaration.Identifier))
                : IdentifierName(externMethodDeclaration.Identifier);
            SyntaxTrivia leadingTrivia = Trivia(
                DocumentationCommentTrivia(SyntaxKind.SingleLineDocumentationCommentTrivia).AddContent(
                    XmlText("/// "),
                    XmlEmptyElement("inheritdoc").AddAttributes(XmlCrefAttribute(NameMemberCref(docRefExternName, ToCref(externMethodDeclaration.ParameterList)))),
                    XmlText().AddTextTokens(XmlTextNewLine("\n", continueXmlDocumentationComment: false))));
            InvocationExpressionSyntax externInvocation = InvocationExpression(
                overloadOf switch
                {
                    FriendlyOverloadOf.ExternMethod => QualifiedName(declaringTypeName, IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.StructMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    FriendlyOverloadOf.InterfaceMethod => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("@this"), IdentifierName(externMethodDeclaration.Identifier.Text)),
                    _ => throw new NotSupportedException("Unrecognized friendly overload mode " + overloadOf),
                })
                .WithArgumentList(FixTrivia(ArgumentList().AddArguments(arguments.ToArray())));
            bool hasVoidReturn = externMethodReturnType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } };
            BlockSyntax? body = Block().AddStatements(leadingStatements.ToArray());
            IdentifierNameSyntax resultLocal = IdentifierName("__result");
            if (returnSafeHandleType is object)
            {
                //// HANDLE result = invocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType)
                    .AddVariables(VariableDeclarator(resultLocal.Identifier).WithInitializer(EqualsValueClause(externInvocation)))));

                body = body.AddStatements(trailingStatements.ToArray());

                //// return new SafeHandle(result, ownsHandle: true);
                body = body.AddStatements(ReturnStatement(ObjectCreationExpression(returnSafeHandleType).AddArgumentListArguments(
                    Argument(GetIntPtrFromTypeDef(resultLocal, originalSignature.ReturnType)),
                    Argument(LiteralExpression(doNotRelease ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)).WithNameColon(NameColon(IdentifierName("ownsHandle"))))));
            }
            else if (hasVoidReturn)
            {
                body = body.AddStatements(ExpressionStatement(externInvocation));
                body = body.AddStatements(trailingStatements.ToArray());
            }
            else
            {
                // var result = externInvocation();
                body = body.AddStatements(LocalDeclarationStatement(VariableDeclaration(externMethodReturnType)
                    .AddVariables(VariableDeclarator(resultLocal.Identifier).WithInitializer(EqualsValueClause(externInvocation)))));

                body = body.AddStatements(trailingStatements.ToArray());

                // return result;
                body = body.AddStatements(ReturnStatement(resultLocal));
            }

            foreach (VariableDeclarationSyntax? fixedExpression in fixedBlocks)
            {
                body = Block(FixedStatement(fixedExpression, body).WithFixedKeyword(TokenWithSpace(SyntaxKind.FixedKeyword)));
            }

            if (finallyStatements.Count > 0)
            {
                body = Block()
                    .AddStatements(leadingOutsideTryStatements.ToArray())
                    .AddStatements(TryStatement(body, default, FinallyClause(Block().AddStatements(finallyStatements.ToArray()))));
            }
            else if (leadingOutsideTryStatements.Count > 0)
            {
                body = body.WithStatements(body.Statements.InsertRange(0, leadingOutsideTryStatements));
            }

            SyntaxTokenList modifiers = TokenList(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword));
            if (overloadOf != FriendlyOverloadOf.StructMethod)
            {
                modifiers = modifiers.Insert(1, TokenWithSpace(SyntaxKind.StaticKeyword));
            }

            if (overloadOf == FriendlyOverloadOf.InterfaceMethod)
            {
                parameters.Insert(0, Parameter(Identifier("@this")).WithType(declaringTypeName.WithTrailingTrivia(TriviaList(Space))).AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword)));
            }

            body = body
                .WithOpenBraceToken(Token(TriviaList(LineFeed), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(TokenWithLineFeed(SyntaxKind.CloseBraceToken));

            MethodDeclarationSyntax friendlyDeclaration = externMethodDeclaration
                .WithReturnType(externMethodReturnType.WithTrailingTrivia(TriviaList(Space)))
                .WithIdentifier(friendlyMethodName)
                .WithModifiers(modifiers)
                .WithAttributeLists(List<AttributeListSyntax>())
                .WithParameterList(FixTrivia(ParameterList().AddParameters(parameters.ToArray())))
                .WithBody(body)
                .WithSemicolonToken(default);

            if (returnSafeHandleType is object)
            {
                friendlyDeclaration = friendlyDeclaration.WithReturnType(returnSafeHandleType.WithTrailingTrivia(TriviaList(Space)));
            }

            if (this.GetSupportedOSPlatformAttribute(methodDefinition.GetCustomAttributes()) is AttributeSyntax supportedOSPlatformAttribute)
            {
                friendlyDeclaration = friendlyDeclaration.AddAttributeLists(AttributeList().AddAttributes(supportedOSPlatformAttribute));
            }

            friendlyDeclaration = friendlyDeclaration
                .WithLeadingTrivia(leadingTrivia);

            yield return friendlyDeclaration;
        }

        ExpressionSyntax GetIntPtrFromTypeDef(ExpressionSyntax typedefValue, TypeHandleInfo typeDefTypeInfo)
        {
            ExpressionSyntax intPtrValue = typedefValue;
            if (this.TryGetTypeDefFieldType(typeDefTypeInfo, out TypeHandleInfo? returnTypeField) && returnTypeField is PrimitiveTypeHandleInfo primitiveReturnField)
            {
                switch (primitiveReturnField.PrimitiveTypeCode)
                {
                    case PrimitiveTypeCode.UInt32:
                        // (IntPtr)result.Value;
                        intPtrValue = CastExpression(IntPtrTypeSyntax, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedefValue, IdentifierName("Value")));
                        break;
                    case PrimitiveTypeCode.UIntPtr:
                        // unchecked((IntPtr)(long)(ulong)result.Value)
                        intPtrValue = UncheckedExpression(
                            CastExpression(
                                IntPtrTypeSyntax,
                                CastExpression(
                                    PredefinedType(Token(SyntaxKind.LongKeyword)),
                                    CastExpression(
                                        PredefinedType(Token(SyntaxKind.ULongKeyword)),
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedefValue, IdentifierName("Value"))))));
                        break;
                }
            }

            return intPtrValue;
        }
#pragma warning restore SA1114 // Parameter list should follow declaration
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

    private ParameterListSyntax CreateParameterList(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature, TypeSyntaxSettings typeSettings)
        => ParameterList().AddParameters(methodDefinition.GetParameters().Select(this.Reader.GetParameter).Where(p => !p.Name.IsNil).Select(p => this.CreateParameter(signature.ParameterTypes[p.SequenceNumber - 1], p, typeSettings)).ToArray());

    private ParameterSyntax CreateParameter(TypeHandleInfo parameterInfo, Parameter parameter, TypeSyntaxSettings typeSettings)
    {
        string name = this.Reader.GetString(parameter.Name);
        try
        {
            // TODO:
            // * Notice [Out][RAIIFree] handle producing parameters. Can we make these provide SafeHandle's?
            bool isReturnOrOutParam = parameter.SequenceNumber == 0 || (parameter.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out;
            TypeSyntaxAndMarshaling parameterTypeSyntax = parameterInfo.ToTypeSyntax(typeSettings, parameter.GetCustomAttributes(), parameter.Attributes);

            // Determine the custom attributes to apply.
            AttributeListSyntax? attributes = AttributeList();
            if (parameterTypeSyntax.Type is PointerTypeSyntax ptr)
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

    private (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) ReinterpretFieldType(FieldDefinition fieldDef, TypeSyntax originalType, CustomAttributeHandleCollection customAttributes, Context context)
    {
        TypeSyntaxSettings typeSettings = context.Filter(this.fieldTypeSettings);
        TypeHandleInfo fieldTypeHandleInfo = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null);
        AttributeSyntax? marshalAs = null;

        // If the field is a fixed length array, we have to work some code gen magic since C# does not allow those.
        if (originalType is ArrayTypeSyntax arrayType && arrayType.RankSpecifiers.Count > 0 && arrayType.RankSpecifiers[0].Sizes.Count == 1)
        {
            return this.DeclareFixedLengthArrayStruct(fieldDef, customAttributes, fieldTypeHandleInfo, arrayType, context);
        }

        // If the field is a delegate type, we have to replace that with a native function pointer to avoid the struct becoming a 'managed type'.
        if ((!context.AllowMarshaling) && this.IsDelegateReference(fieldTypeHandleInfo, out TypeDefinition typeDef) && !this.IsUntypedDelegate(typeDef))
        {
            return (this.FunctionPointer(typeDef), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        // If the field is a pointer to a COM interface (and we're using bona fide interfaces),
        // then we must type it as an array.
        if (context.AllowMarshaling && fieldTypeHandleInfo is PointerTypeHandleInfo ptr3 && this.IsManagedType(ptr3.ElementType))
        {
            return (ArrayType(ptr3.ElementType.ToTypeSyntax(typeSettings, null).Type).AddRankSpecifiers(ArrayRankSpecifier()), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        return (originalType, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
    }

    private (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) DeclareFixedLengthArrayStruct(FieldDefinition fieldDef, CustomAttributeHandleCollection customAttributes, TypeHandleInfo fieldTypeHandleInfo, ArrayTypeSyntax arrayType, Context context)
    {
        if (this.options.AllowMarshaling && this.IsManagedType(fieldTypeHandleInfo))
        {
            ArrayTypeSyntax ranklessArray = arrayType.WithRankSpecifiers(new SyntaxList<ArrayRankSpecifierSyntax>(ArrayRankSpecifier()));
            AttributeSyntax marshalAs = MarshalAs(UnmanagedType.ByValArray, sizeConst: arrayType.RankSpecifiers[0].Sizes[0]);
            return (ranklessArray, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        int length = int.Parse(((LiteralExpressionSyntax)arrayType.RankSpecifiers[0].Sizes[0]).Token.ValueText, CultureInfo.InvariantCulture);
        TypeSyntax elementType = arrayType.ElementType;

        static string SanitizeTypeName(string typeName) => typeName.Replace(' ', '_').Replace('.', '_').Replace(':', '_').Replace('*', '_').Replace('<', '_').Replace('>', '_').Replace('[', '_').Replace(']', '_').Replace(',', '_');
        void DetermineNames(TypeSyntax elementType, out string? structNamespace, out string fixedLengthStructNameString, out string? fileNamePrefix)
        {
            if (elementType is QualifiedNameSyntax qualifiedElementType)
            {
                structNamespace = qualifiedElementType.Left.ToString();
                if (!structNamespace.StartsWith(GlobalWinmdRootNamespaceAlias))
                {
                    // Force structs to be under the root namespace.
                    structNamespace = GlobalWinmdRootNamespaceAlias;
                }

                fileNamePrefix = SanitizeTypeName(qualifiedElementType.Right.Identifier.ValueText);
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is PredefinedTypeSyntax predefined)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = predefined.Keyword.ValueText;
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is IdentifierNameSyntax identifier)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = identifier.Identifier.ValueText;
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is FunctionPointerTypeSyntax functionPtr)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = "FunctionPointer";
                fixedLengthStructNameString = $"__{SanitizeTypeName(functionPtr.ToString())}_{length}";
            }
            else if (elementType is PointerTypeSyntax elementPointerType)
            {
                DetermineNames(elementPointerType.ElementType, out structNamespace, out fixedLengthStructNameString, out fileNamePrefix);
                fixedLengthStructNameString = $"P{fixedLengthStructNameString}";
            }
            else
            {
                throw new NotSupportedException($"Type {elementType} had unexpected kind: {elementType.GetType().Name}");
            }

            // Generate inline array as a nested struct if the element type is itself a nested type.
            if (fieldTypeHandleInfo is ArrayTypeHandleInfo { ElementType: HandleTypeHandleInfo fieldHandleTypeInfo } && this.IsNestedType(fieldHandleTypeInfo.Handle))
            {
                structNamespace = null;
                fileNamePrefix = null;
            }
        }

        DetermineNames(elementType, out string? structNamespace, out string fixedLengthStructNameString, out string? fileNamePrefix);
        IdentifierNameSyntax fixedLengthStructName = IdentifierName(fixedLengthStructNameString);
        TypeSyntax qualifiedFixedLengthStructName = ParseTypeName($"{structNamespace}.{fixedLengthStructNameString}");

        if (structNamespace is not null && this.volatileCode.IsInlineArrayStructGenerated(structNamespace, fixedLengthStructNameString))
        {
            return (qualifiedFixedLengthStructName, default, default);
        }

        // IntPtr/UIntPtr began implementing IEquatable<T> in .NET 5. We may want to actually resolve the type in the compilation to see if it implements this.
        bool elementTypeIsEquatable = elementType is PredefinedTypeSyntax;
        bool fixedArrayAllowed = elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword or (int)SyntaxKind.ByteKeyword or (int)SyntaxKind.ShortKeyword or (int)SyntaxKind.IntKeyword or (int)SyntaxKind.LongKeyword or (int)SyntaxKind.CharKeyword or (int)SyntaxKind.SByteKeyword or (int)SyntaxKind.UShortKeyword or (int)SyntaxKind.UIntKeyword or (int)SyntaxKind.ULongKeyword or (int)SyntaxKind.FloatKeyword or (int)SyntaxKind.DoubleKeyword };

        // internal struct __TheStruct_Count
        // {
        //     internal unsafe fixed TheStruct Value[LENGTH];
        //     /// <summary>The length of the inline array.</summary>
        //     internal const int Length = LENGTH;
        // ...
        IdentifierNameSyntax lengthConstant = IdentifierName("SpanLength");
        IdentifierNameSyntax lengthInstanceProperty = IdentifierName("Length");

        // private const int SpanLength = 8;
        MemberDeclarationSyntax spanLengthDeclaration = FieldDeclaration(VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)))
            .AddVariables(VariableDeclarator(lengthConstant.Identifier)
                .WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(length))))))
            .AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword), TokenWithSpace(SyntaxKind.ConstKeyword));

        //// internal readonly int Length => SpanLength;
        MemberDeclarationSyntax lengthDeclaration = PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), lengthInstanceProperty.Identifier)
            .WithExpressionBody(ArrowExpressionClause(lengthConstant))
            .WithSemicolonToken(Semicolon)
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
            .WithLeadingTrivia(Trivia(DocumentationCommentTrivia(SyntaxKind.SingleLineDocumentationCommentTrivia).AddContent(
                DocCommentStart,
                XmlElement("summary", List(new XmlNodeSyntax[]
                {
                            XmlText("The length of the inline array."),
                })),
                DocCommentEnd)));

        StructDeclarationSyntax? fixedLengthStruct = StructDeclaration(fixedLengthStructName.Identifier)
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword))
            .AddMembers(
                spanLengthDeclaration,
                lengthDeclaration);

        IdentifierNameSyntax? valueFieldName = null;
        IdentifierNameSyntax? firstElementName = null;
        if (fixedArrayAllowed)
        {
            // internal unsafe fixed TheStruct Value[SpanLength];
            valueFieldName = IdentifierName("Value");
            fixedLengthStruct = fixedLengthStruct.AddMembers(
                FieldDeclaration(VariableDeclaration(elementType)
                    .AddVariables(VariableDeclarator(valueFieldName.Identifier).AddArgumentListArguments(
                        Argument(lengthConstant))))
                    .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.FixedKeyword)));
        }
        else
        {
            // internal TheStruct _0, _1, _2, ...;
            firstElementName = IdentifierName("_0");
            FieldDeclarationSyntax fieldDecl = FieldDeclaration(VariableDeclaration(elementType)
                .AddVariables(Enumerable.Range(0, length).Select(i => VariableDeclarator(Identifier(Invariant($"_{i}")))).ToArray()))
                .AddModifiers(TokenWithSpace(this.Visibility));
            if (RequiresUnsafe(elementType))
            {
                fieldDecl = fieldDecl.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(fieldDecl);
        }

        // fixed (TheStruct* p0 = Value) ...
        // - or - (depending on fixed array field use)
        // fixed (TheStruct* p0 = &_0) ...
        FixedStatementSyntax FixedBlock(SyntaxToken pointerLocalIdentifier, StatementSyntax body) =>
            FixedStatement(
                VariableDeclaration(PointerType(elementType)).AddVariables(VariableDeclarator(pointerLocalIdentifier).WithInitializer(EqualsValueClause((ExpressionSyntax?)valueFieldName ?? PrefixUnaryExpression(SyntaxKind.AddressOfExpression, firstElementName!)))),
                body);

        if (valueFieldName is not null)
        {
            // [UnscopedRef] internal unsafe ref TheStruct this[int index] => ref Value[index];
            IndexerDeclarationSyntax indexer = IndexerDeclaration(RefType(elementType).WithTrailingTrivia(TriviaList(Space)))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddParameterListParameters(Parameter(Identifier("index")).WithType(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword))))
                .WithExpressionBody(ArrowExpressionClause(RefExpression(
                    ElementAccessExpression(valueFieldName).AddArgumentListArguments(Argument(IdentifierName("index"))))))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList().AddAttributes(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeIndexerComment);
            fixedLengthStruct = fixedLengthStruct.AddMembers(indexer);
            this.DeclareUnscopedRefAttributeIfNecessary();
        }
        else
        {
            // internal unsafe char this[int index]
            ////readonly get
            ////{
            ////    fixed (char* p0 = &_0)
            ////        return new Span<char>(p0, SpanLength)[index];
            ////}

            ////set
            ////{
            ////    fixed (char* p0 = &_0)
            ////        new Span<char>(p0, SpanLength)[index] = value;
            ////}
        }

        IdentifierNameSyntax? asReadOnlyMethodName = null;

        // Pointers cannot be used as type arguments, so if the element type is unsafe (a pointer), we have to skip the Span<T> methods.
        // We could overcome this by defining a PElementType struct that contains the pointer, then use the PElementType as the type argument.
        if (this.canCallCreateSpan && !RequiresUnsafe(elementType))
        {
            // Value[0]
            ExpressionSyntax value0 = valueFieldName is not null
                ? ElementAccessExpression(valueFieldName).AddArgumentListArguments(Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))
                : firstElementName!;

            // ref Value[0]
            ArgumentSyntax refValue0 = Argument(nameColon: null, TokenWithSpace(SyntaxKind.RefKeyword), value0);

            // MemoryMarshal.CreateSpan(ref Value[0], Length)
            InvocationExpressionSyntax createSpanInvocation = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("MemoryMarshal"), IdentifierName("CreateSpan")))
                .WithArgumentList(FixTrivia(ArgumentList().AddArguments(refValue0, Argument(lengthConstant))));

            // [UnscopedRef] internal unsafe Span<TheStruct> AsSpan() => MemoryMarshal.CreateSpan(ref Value[0], Length);
            MethodDeclarationSyntax asSpanMethod = MethodDeclaration(MakeSpanOfT(elementType).WithTrailingTrivia(TriviaList(Space)), Identifier("AsSpan"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .WithExpressionBody(ArrowExpressionClause(createSpanInvocation))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList().AddAttributes(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeAsSpanComment);
            this.DeclareUnscopedRefAttributeIfNecessary();

            // ref Unsafe.AsRef(Value[0])
            ArgumentSyntax refUnsafeValue0 = Argument(
                nameColon: null,
                TokenWithSpace(SyntaxKind.RefKeyword),
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), IdentifierName(nameof(Unsafe.AsRef))))
                    .WithArgumentList(ArgumentList().AddArguments(Argument(value0))));

            // MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(Value[0]), Length)
            InvocationExpressionSyntax createReadOnlySpanInvocation = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("MemoryMarshal"), IdentifierName("CreateReadOnlySpan")))
                .WithArgumentList(FixTrivia(ArgumentList().AddArguments(refUnsafeValue0, Argument(lengthConstant))));

            // [UnscopedRef] internal unsafe readonly ReadOnlySpan<TheStruct> AsReadOnlySpan() => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(Value[0]), Length);
            asReadOnlyMethodName = IdentifierName("AsReadOnlySpan");
            MethodDeclarationSyntax asReadOnlySpanMethod = MethodDeclaration(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(TriviaList(Space)), asReadOnlyMethodName.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithExpressionBody(ArrowExpressionClause(createReadOnlySpanInvocation))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList().AddAttributes(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeAsSpanComment);

            fixedLengthStruct = fixedLengthStruct.AddMembers(asSpanMethod, asReadOnlySpanMethod);
        }

#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1114 // Parameter list should follow declaration

        bool generateSpanLegacyHelpers = this.canUseSpan && (!this.canCallCreateSpan || this.Options.MultiTargetingFriendlyAPIs);
        if (generateSpanLegacyHelpers && !RequiresUnsafe(elementType))
        {
            // internal readonly void CopyTo(Span<TheStruct> target, int length = Length)
            IdentifierNameSyntax targetParameterName = IdentifierName("target");
            IdentifierNameSyntax lengthParameterName = IdentifierName("length");
            IdentifierNameSyntax copyToMethodName = IdentifierName("CopyTo");
            MethodDeclarationSyntax copyToMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), copyToMethodName.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(targetParameterName.Identifier).WithType(MakeSpanOfT(elementType).WithTrailingTrivia(Space)),
                    Parameter(lengthParameterName.Identifier).WithType(PredefinedType(Token(SyntaxKind.IntKeyword)).WithTrailingTrivia(Space)).WithDefault(EqualsValueClause(lengthConstant)));

            // x.Slice(0, length).CopyTo(target)
            InvocationExpressionSyntax CopyToExpression(ExpressionSyntax readOnlySpanExpression) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(ReadOnlySpan<int>.Slice))),
                            ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))), Argument(lengthParameterName))),
                        IdentifierName(nameof(ReadOnlySpan<int>.CopyTo))),
                    ArgumentList().AddArguments(Argument(targetParameterName)));

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).CopyTo(target);
                copyToMethod = copyToMethod
                    .WithExpressionBody(ArrowExpressionClause(CopyToExpression(InvocationExpression(asReadOnlyMethodName, ArgumentList()))))
                    .WithSemicolonToken(SemicolonWithLineFeed);
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                copyToMethod = copyToMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block().AddStatements(
                    //// fixed (TheStruct* p0 = Value) new ReadOnlySpan<char>(p0, Length).Slice(0, length).CopyTo(target);
                    FixedBlock(
                        p0Local.Identifier,
                        ExpressionStatement(CopyToExpression(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType)).AddArgumentListArguments(Argument(p0Local), Argument(lengthConstant)))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(copyToMethod);

            // internal readonly TheStruct[] ToArray(int length = Length)
            MethodDeclarationSyntax toArrayMethod = MethodDeclaration(ArrayType(elementType, SingletonList(ArrayRankSpecifier())), Identifier("ToArray"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(lengthParameterName.Identifier).WithType(PredefinedType(Token(SyntaxKind.IntKeyword)).WithTrailingTrivia(Space)).WithDefault(EqualsValueClause(lengthConstant)));

            // x.Slice(0, length).ToArray()
            InvocationExpressionSyntax ToArrayExpression(ExpressionSyntax readOnlySpanExpression) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(ReadOnlySpan<int>.Slice))),
                            ArgumentList().AddArguments(Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))), Argument(lengthParameterName))),
                        IdentifierName(nameof(ReadOnlySpan<int>.ToArray))),
                    ArgumentList());

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).ToArray()
                toArrayMethod = toArrayMethod
                    .WithExpressionBody(ArrowExpressionClause(ToArrayExpression(InvocationExpression(asReadOnlyMethodName, ArgumentList()))))
                    .WithSemicolonToken(SemicolonWithLineFeed);
                if (RequiresUnsafe(elementType))
                {
                    toArrayMethod = toArrayMethod.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                }
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toArrayMethod = toArrayMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block().AddStatements(
                    //// fixed (TheStruct* p0 = Value)
                    FixedBlock(
                        p0Local.Identifier,
                        //// return new ReadOnlySpan<char>(p0, Length).Slice(0, length).ToArray();
                        ReturnStatement(ToArrayExpression(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType)).AddArgumentListArguments(Argument(p0Local), Argument(lengthConstant)))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toArrayMethod);
        }

        if (this.canUseSpan && elementTypeIsEquatable)
        {
            // internal readonly bool Equals(ReadOnlySpan<TheStruct> value)
            IdentifierNameSyntax valueParameterName = IdentifierName("value");
            MethodDeclarationSyntax equalsSpanMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.BoolKeyword)), Identifier(nameof(object.Equals)))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(valueParameterName.Identifier).WithType(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(Space)));

            ExpressionSyntax EqualsBoolExpression(ExpressionSyntax readOnlySpanExpression) => elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.CharKeyword }
                ? ConditionalExpression(
                    //// value.Length == Length
                    BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameterName, IdentifierName(nameof(ReadOnlySpan<int>.Length))),
                        lengthConstant),
                    //// span.SequenceEqual(value)
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(Enumerable.SequenceEqual))),
                        ArgumentList().AddArguments(Argument(valueParameterName))),
                    //// span.SliceAtNull().SequenceEqual(value)
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, SliceAtNullMethodName), ArgumentList()),
                            IdentifierName(nameof(Enumerable.SequenceEqual))),
                        ArgumentList().AddArguments(Argument(valueParameterName))))
                : InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(Enumerable.SequenceEqual))),
                        ArgumentList().AddArguments(Argument(valueParameterName))); // span.SequenceEqual(value);
            this.DeclareSliceAtNullExtensionMethodIfNecessary();

            if (asReadOnlyMethodName is not null)
            {
                // => value.Length == Length ? AsReadOnlySpan().SequenceEqual(value) : AsReadOnlySpan().SliceAtNull().SequenceEqual(value);
                equalsSpanMethod = equalsSpanMethod
                    .WithExpressionBody(ArrowExpressionClause(EqualsBoolExpression(InvocationExpression(asReadOnlyMethodName, ArgumentList()))))
                    .WithSemicolonToken(Semicolon);
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                IdentifierNameSyntax spanLocal = IdentifierName("span");
                equalsSpanMethod = equalsSpanMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block().AddStatements(
                        // fixed (TheStruct* p0 = Value)
                        FixedBlock(
                            p0Local.Identifier,
                            Block().AddStatements(
                                // ReadOnlySpan<char> span = new(p0, Length);
                                LocalDeclarationStatement(VariableDeclaration(MakeReadOnlySpanOfT(elementType)).AddVariables(
                                    VariableDeclarator(spanLocal.Identifier).WithInitializer(EqualsValueClause(
                                        ObjectCreationExpression(MakeReadOnlySpanOfT(elementType)).AddArgumentListArguments(Argument(p0Local), Argument(lengthConstant)))))),
                                // return value.Length == Length ? span.SequenceEqual(value) : span.SliceAtNull().SequenceEqual(value);
                                ReturnStatement(EqualsBoolExpression(spanLocal))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(equalsSpanMethod);
        }

#pragma warning restore SA1114 // Parameter list should follow declaration
#pragma warning restore SA1515 // Single-line comment should be preceded by blank line

        if (elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.CharKeyword })
        {
            if (this.canUseSpan)
            {
                // internal readonly bool Equals(string value) => Equals(value.AsSpan());
                fixedLengthStruct = fixedLengthStruct.AddMembers(
                    MethodDeclaration(PredefinedType(Token(SyntaxKind.BoolKeyword)), Identifier("Equals"))
                        .AddModifiers(Token(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                        .AddParameterListParameters(Parameter(Identifier("value")).WithType(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword))))
                        .WithExpressionBody(ArrowExpressionClause(InvocationExpression(
                            IdentifierName("Equals"),
                            ArgumentList().AddArguments(Argument(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("value"), IdentifierName("AsSpan")),
                                    ArgumentList()))))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // internal unsafe readonly string ToString(int length)
            IdentifierNameSyntax lengthParameterName = IdentifierName("length");
            MethodDeclarationSyntax toStringLengthMethod =
                MethodDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)), Identifier(nameof(this.ToString)))
                    .AddModifiers(Token(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .AddParameterListParameters(
                        Parameter(lengthParameterName.Identifier).WithType(PredefinedType(Token(SyntaxKind.IntKeyword)).WithTrailingTrivia(Space)))
                    .WithLeadingTrivia(InlineCharArrayToStringWithLengthComment);

            // x.Slice(0, length).ToString()
            InvocationExpressionSyntax SliceAtLengthToString(ExpressionSyntax readOnlySpan) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpan, IdentifierName("Slice")),
                            ArgumentList().AddArguments(
                                Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                Argument(lengthParameterName))),
                        IdentifierName(nameof(object.ToString))),
                    ArgumentList());

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).ToString()
                toStringLengthMethod = toStringLengthMethod
                    .WithExpressionBody(ArrowExpressionClause(SliceAtLengthToString(InvocationExpression(asReadOnlyMethodName, ArgumentList()))))
                    .WithSemicolonToken(Semicolon);
            }
            else if (this.canUseSpan)
            {
                // fixed (char* p0 = Value) return new ReadOnlySpan<char>(p0, Length).Slice(0, length).ToString();
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringLengthMethod = toStringLengthMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block().AddStatements(
                        FixedBlock(
                            p0Local.Identifier,
                            ReturnStatement(SliceAtLengthToString(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType)).AddArgumentListArguments(Argument(p0Local), Argument(lengthConstant)))))));
            }
            else
            {
                // if (length < 0 || length > Length)
                //     throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be between 0 and the fixed array length.");
                // fixed (char* p0 = this.Value)
                //     return new string(p0, 0, length);
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringLengthMethod = toStringLengthMethod
                     .AddModifiers(Token(SyntaxKind.UnsafeKeyword))
                     .WithBody(Block(
                         IfStatement(
                             BinaryExpression(
                                 SyntaxKind.LogicalOrExpression,
                                 BinaryExpression(SyntaxKind.LessThanExpression, lengthParameterName, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                 BinaryExpression(SyntaxKind.GreaterThanExpression, lengthParameterName, lengthConstant)),
                             ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentOutOfRangeException))).AddArgumentListArguments(
                                 Argument(NameOfExpression(lengthParameterName)),
                                 Argument(lengthParameterName),
                                 Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Length must be between 0 and the fixed array length, inclusive.")))))),
                         FixedBlock(
                             p0Local.Identifier,
                             ReturnStatement(
                                 ObjectCreationExpression(PredefinedType(Token(SyntaxKind.StringKeyword))).AddArgumentListArguments(
                                     Argument(p0Local),
                                     Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                     Argument(lengthParameterName))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toStringLengthMethod);

            // public override readonly string ToString()
            MethodDeclarationSyntax toStringOverride =
                MethodDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)), Identifier(nameof(this.ToString)))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.OverrideKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .WithLeadingTrivia(InlineCharArrayToStringComment);

            // x.SliceAtNull().ToString()
            InvocationExpressionSyntax SliceAtNullToString(ExpressionSyntax readOnlySpan)
            {
                this.DeclareSliceAtNullExtensionMethodIfNecessary();
                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpan, SliceAtNullMethodName),
                            ArgumentList()),
                        IdentifierName(nameof(object.ToString))),
                    ArgumentList());
            }

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().SliceAtNull().ToString();
                toStringOverride = toStringOverride
                    .WithExpressionBody(ArrowExpressionClause(SliceAtNullToString(InvocationExpression(asReadOnlyMethodName, ArgumentList()))))
                    .WithSemicolonToken(Semicolon);
            }
            else if (this.canUseSpan)
            {
                // fixed (char* p0 = Value) return new ReadOnlySpan<char>(p0, Length).SliceAtNull().ToString();
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringOverride = toStringOverride
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block().AddStatements(
                        FixedBlock(
                            p0Local.Identifier,
                            ReturnStatement(SliceAtNullToString(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType)).AddArgumentListArguments(Argument(p0Local), Argument(lengthConstant)))))));
            }
            else
            {
                IdentifierNameSyntax lengthLocalVar = IdentifierName("length");
                StatementSyntax[] lengthDeclarationStatements;

                // int length;
                // fixed (char* p = Value)
                // {
                //     char* pLastExclusive = p + Length;
                //     char* pCh = p;
                //     for (; pCh < pLastExclusive && *pCh != '\0'; pCh++);
                //     length = checked((int)(pCh - p));
                // }
                IdentifierNameSyntax p = IdentifierName("p");
                IdentifierNameSyntax pLastExclusive = IdentifierName("pLastExclusive");
                IdentifierNameSyntax pCh = IdentifierName("pCh");
                lengthDeclarationStatements = new StatementSyntax[]
                {
                    LocalDeclarationStatement(VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword))).AddVariables(
                            VariableDeclarator(lengthLocalVar.Identifier))),
                    FixedBlock(
                        p.Identifier,
                        Block().AddStatements(
                            LocalDeclarationStatement(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                                VariableDeclarator(pLastExclusive.Identifier).WithInitializer(EqualsValueClause(BinaryExpression(SyntaxKind.AddExpression, p, IdentifierName("Length")))))),
                            LocalDeclarationStatement(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                                VariableDeclarator(pCh.Identifier).WithInitializer(EqualsValueClause(p)))),
                            ForStatement(
                                null,
                                BinaryExpression(
                                        SyntaxKind.LogicalAndExpression,
                                        BinaryExpression(
                                            SyntaxKind.LessThanExpression,
                                            pCh,
                                            pLastExclusive),
                                        BinaryExpression(
                                            SyntaxKind.NotEqualsExpression,
                                            PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, pCh),
                                            LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))),
                                SingletonSeparatedList<ExpressionSyntax>(PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, pCh)),
                                EmptyStatement()),
                            ExpressionStatement(AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                lengthLocalVar,
                                CheckedExpression(CastExpression(
                                    PredefinedType(Token(SyntaxKind.IntKeyword)),
                                    ParenthesizedExpression(BinaryExpression(SyntaxKind.SubtractExpression, pCh, p)))))))),
                };

                // return ToString(length);
                toStringOverride = toStringOverride
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(lengthDeclarationStatements).AddStatements(
                        ReturnStatement(InvocationExpression(
                            IdentifierName("ToString"),
                            ArgumentList().AddArguments(Argument(lengthLocalVar))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toStringOverride);

            if (this.canUseSpan)
            {
                // public static implicit operator __char_64(string? value) => value.AsSpan();
                fixedLengthStruct = fixedLengthStruct.AddMembers(
                    ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fixedLengthStructName)
                        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                        .AddParameterListParameters(Parameter(Identifier("value")).WithType(PredefinedType(Token(SyntaxKind.StringKeyword)).WithTrailingTrivia(TriviaList(Space))))
                        .WithExpressionBody(ArrowExpressionClause(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("value"), IdentifierName(nameof(MemoryExtensions.AsSpan))))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // Make sure .NET marshals these `char` arrays as UTF-16.
            fixedLengthStruct = fixedLengthStruct
                .AddAttributeLists(AttributeList().AddAttributes(StructLayout(TypeAttributes.SequentialLayout, charSet: CharSet.Unicode)));
        }

        // public static implicit operator __TheStruct_64(ReadOnlySpan<TheStruct> value)
        if (this.canUseSpan && !RequiresUnsafe(elementType))
        {
            IdentifierNameSyntax valueParam = IdentifierName("value");
            ConversionOperatorDeclarationSyntax implicitSpanToStruct =
                ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fixedLengthStructName)
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                    .AddParameterListParameters(Parameter(valueParam.Identifier).WithType(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(TriviaList(Space))))
                    .WithBody(Block());

            IdentifierNameSyntax resultLocal = IdentifierName("result");
            IdentifierNameSyntax initLengthLocal = IdentifierName("initLength");

            ExpressionSyntax firstElement = valueFieldName is not null
                ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, valueFieldName) // result.Value
                : PrefixUnaryExpression(SyntaxKind.AddressOfExpression, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, firstElementName!)); // &result._0

            // Unsafe.SkipInit(out __char_1 result);
            implicitSpanToStruct = implicitSpanToStruct.AddBodyStatements(
                ExpressionStatement(InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), IdentifierName("SkipInit")),
                    ArgumentList().AddArguments(Argument(nameColon: null, Token(SyntaxKind.OutKeyword), DeclarationExpression(fixedLengthStructName.WithTrailingTrivia(Space), SingleVariableDesignation(resultLocal.Identifier)))))));

            // x.Slice(initLength, Length - initLength).Clear();
            StatementSyntax ClearSlice(ExpressionSyntax span) =>
                ExpressionStatement(InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<int>.Slice))),
                            ArgumentList().AddArguments(
                                Argument(initLengthLocal),
                                Argument(BinaryExpression(SyntaxKind.SubtractExpression, lengthConstant, initLengthLocal)))),
                        IdentifierName(nameof(Span<int>.Clear))),
                    ArgumentList()));

            if (this.canUseSpan)
            {
                //// int initLength = value.Length;
                LocalDeclarationStatementSyntax declareInitLength =
                    LocalDeclarationStatement(VariableDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword))).AddVariables(
                        VariableDeclarator(initLengthLocal.Identifier).WithInitializer(EqualsValueClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<int>.Length)))))));

                if (this.canCallCreateSpan)
                {
                    // value.CopyTo(result.AsSpan());
                    StatementSyntax valueCopyToResult =
                        ExpressionStatement(InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<int>.CopyTo))),
                            ArgumentList().AddArguments(Argument(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, IdentifierName("AsSpan")), ArgumentList())))));
                    implicitSpanToStruct = implicitSpanToStruct
                        .AddBodyStatements(
                            valueCopyToResult,
                            declareInitLength,
                            //// result.AsSpan().Slice(initLength, Length - initLength).Clear();
                            ClearSlice(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, IdentifierName("AsSpan")), ArgumentList())));
                }
                else
                {
                    IdentifierNameSyntax targetLocal = IdentifierName("target");

                    // Span<char> target = new Span<char>(result.Value, Length);
                    StatementSyntax declareTargetLocal =
                        LocalDeclarationStatement(VariableDeclaration(MakeSpanOfT(elementType)).AddVariables(
                                VariableDeclarator(targetLocal.Identifier).WithInitializer(EqualsValueClause(
                                    ObjectCreationExpression(MakeSpanOfT(elementType)).AddArgumentListArguments(
                                        Argument(firstElement),
                                        Argument(lengthConstant))))));

                    implicitSpanToStruct = implicitSpanToStruct
                        .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                        .AddBodyStatements(
                            declareTargetLocal,
                            ////value.CopyTo(target);
                            ExpressionStatement(InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<int>.CopyTo))),
                                ArgumentList().AddArguments(Argument(targetLocal)))),
                            declareInitLength,
                            ////target.Slice(initLength, Length - initLength).Clear();
                            ClearSlice(targetLocal));
                }
            }
            else
            {
                IdentifierNameSyntax pLocal = IdentifierName("p");
                IdentifierNameSyntax iLocal = IdentifierName("i");

                // if (value.Length > result.Length) throw new ArgumentException("Too long");
                StatementSyntax checkRange = IfStatement(
                    BinaryExpression(
                        SyntaxKind.GreaterThanExpression,
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<int>.Length))),
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, lengthConstant)),
                    ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException))).AddArgumentListArguments(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Length exceeds fixed array size."))))));

                implicitSpanToStruct = implicitSpanToStruct
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .AddBodyStatements(
                        checkRange,
                        //// TheStruct* p = result.Value;
                        LocalDeclarationStatement(VariableDeclaration(PointerType(PredefinedType(Token(SyntaxKind.CharKeyword)))).AddVariables(
                            VariableDeclarator(pLocal.Identifier).WithInitializer(EqualsValueClause(firstElement)))),
                        //// for (int i = 0; i < value.Length; i++) *p++ = value[i];
                        ForStatement(
                            VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword))).AddVariables(
                                VariableDeclarator(iLocal.Identifier).WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))),
                            BinaryExpression(SyntaxKind.LessThanExpression, iLocal, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<char>.Length)))),
                            SingletonSeparatedList<ExpressionSyntax>(PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, iLocal)),
                            Block().AddStatements(
                            ExpressionStatement(AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, pLocal)),
                                ElementAccessExpression(valueParam).AddArgumentListArguments(Argument(iLocal)))))));
            }

            // return result;
            implicitSpanToStruct = implicitSpanToStruct.AddBodyStatements(ReturnStatement(resultLocal));

            fixedLengthStruct = fixedLengthStruct
                .AddMembers(implicitSpanToStruct);
        }

        // internal static unsafe ref readonly TheStruct ReadOnlyItemRef(this in MainAVIHeader.__dwReserved_4 @this, int index)
        if (valueFieldName is not null)
        {
            IdentifierNameSyntax indexParamName = IdentifierName("index");
            IdentifierNameSyntax atThis = IdentifierName("@this");
            TypeSyntax qualifiedElementType;
            if (elementType == IntPtrTypeSyntax)
            {
                qualifiedElementType = elementType;
            }
            else
            {
                qualifiedElementType = fieldTypeHandleInfo.ToTypeSyntax(this.extensionMethodSignatureTypeSettings, customAttributes).Type switch
                {
                    ArrayTypeSyntax at => at.ElementType,
                    PointerTypeSyntax ptrType => ptrType.ElementType,
                    _ => throw new GenerationFailedException($"Unexpected runtime type."),
                };
            }

            TypeSyntaxSettings extensionMethodSignatureTypeSettings = context.Filter(this.extensionMethodSignatureTypeSettings);

            // internal static unsafe ref readonly TheStruct ReadOnlyItemRef(this in MainAVIHeader.__dwReserved_4 @this, int index) => ref @this.Value[index]
            ParameterSyntax thisParameter = Parameter(atThis.Identifier)
                .WithType(qualifiedFixedLengthStructName.WithTrailingTrivia(Space))
                .AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword), TokenWithSpace(SyntaxKind.InKeyword));
            ParameterSyntax indexParameter = Parameter(indexParamName.Identifier).WithType(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)));
            MethodDeclarationSyntax getAtMethod = MethodDeclaration(RefType(qualifiedElementType.WithTrailingTrivia(TriviaList(Space))).WithReadOnlyKeyword(TokenWithSpace(SyntaxKind.ReadOnlyKeyword)), Identifier("ReadOnlyItemRef"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .WithParameterList(FixTrivia(ParameterList().AddParameters(thisParameter, indexParameter)))
                .WithExpressionBody(ArrowExpressionClause(RefExpression(ElementAccessExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, atThis, valueFieldName)).AddArgumentListArguments(Argument(indexParamName)))))
                .WithSemicolonToken(Semicolon);

            this.volatileCode.AddInlineArrayIndexerExtension(getAtMethod);
        }

        if (structNamespace is not null)
        {
            // Wrap with any additional namespaces.
            MemberDeclarationSyntax fixedLengthStructInNamespace = fixedLengthStruct;
            if (structNamespace != GlobalWinmdRootNamespaceAlias)
            {
                if (!structNamespace.StartsWith(GlobalWinmdRootNamespaceAlias + ".", StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"The {structNamespace}.{fixedLengthStructNameString} struct must be under the metadata's common namespace.");
                }

                fixedLengthStructInNamespace = NamespaceDeclaration(ParseName(structNamespace.Substring(GlobalWinmdRootNamespaceAlias.Length + 1)))
                    .AddMembers(fixedLengthStruct);
            }

            fixedLengthStructInNamespace = fixedLengthStructInNamespace
                    .WithAdditionalAnnotations(new SyntaxAnnotation(SimpleFileNameAnnotation, $"{fileNamePrefix}.InlineArrays"));

            this.volatileCode.AddInlineArrayStruct(structNamespace, fixedLengthStructNameString, fixedLengthStructInNamespace);

            return (qualifiedFixedLengthStructName, default, null);
        }
        else
        {
            // This struct will be injected as a nested type, to match the element type.
            return (fixedLengthStructName, List<MemberDeclarationSyntax>().Add(fixedLengthStruct), null);
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

    private void DeclareUnscopedRefAttributeIfNecessary()
    {
        if (this.unscopedRefAttributePredefined)
        {
            return;
        }

        const string name = "UnscopedRefAttribute";
        this.volatileCode.GenerateSpecialType(name, delegate
        {
            ExpressionSyntax[] uses = new[]
            {
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Method))),
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Property))),
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(AttributeTargets)), IdentifierName(nameof(AttributeTargets.Parameter))),
            };
            AttributeListSyntax usageAttr = AttributeList().AddAttributes(
                Attribute(IdentifierName(nameof(AttributeUsageAttribute))).AddArgumentListArguments(
                    AttributeArgument(CompoundExpression(SyntaxKind.BitwiseOrExpression, uses)),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("AllowMultiple"))),
                    AttributeArgument(LiteralExpression(SyntaxKind.FalseLiteralExpression)).WithNameEquals(NameEquals(IdentifierName("Inherited")))));
            ClassDeclarationSyntax attrDecl = ClassDeclaration(Identifier("UnscopedRefAttribute"))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("Attribute")))))
                .AddModifiers(Token(SyntaxKind.InternalKeyword), TokenWithSpace(SyntaxKind.SealedKeyword))
                .AddAttributeLists(usageAttr);
            NamespaceDeclarationSyntax nsDeclaration = NamespaceDeclaration(ParseName("System.Diagnostics.CodeAnalysis"))
                .AddMembers(attrDecl);

            this.volatileCode.AddSpecialType(name, nsDeclaration, topLevel: true);
        });
    }

    private bool TryDeclareCOMGuidInterfaceIfNecessary()
    {
        // Static interface members require C# 11 and .NET 7 at minimum.
        if (!this.IsFeatureAvailable(Feature.InterfaceStaticMembers))
        {
            return false;
        }

        if (this.comIIDInterfacePredefined)
        {
            return true;
        }

        this.volatileCode.GenerateSpecialType(IComIIDGuidInterfaceName.Identifier.ValueText, delegate
        {
            // internal static abstract ref readonly Guid Guid { get; }
            PropertyDeclarationSyntax guidProperty = PropertyDeclaration(IdentifierName(nameof(Guid)).WithTrailingTrivia(Space), ComIIDGuidPropertyName.Identifier)
                .AddModifiers(
                    TokenWithSpace(this.Visibility),
                    TokenWithSpace(SyntaxKind.StaticKeyword),
                    TokenWithSpace(SyntaxKind.AbstractKeyword),
                    TokenWithSpace(SyntaxKind.RefKeyword),
                    TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithAccessorList(AccessorList().AddAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Semicolon)))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>The IID guid for this interface.</summary>\n/// <remarks>The <see cref=\"Guid\" /> reference that is returned comes from a permanent memory address, and is therefore safe to convert to a pointer and pass around or hold long-term.</remarks>\n"));

            // internal interface IComIID { ... }
            InterfaceDeclarationSyntax ifaceDecl = InterfaceDeclaration(IComIIDGuidInterfaceName.Identifier)
                .AddModifiers(Token(this.Visibility))
                .AddMembers(guidProperty);

            this.volatileCode.AddSpecialType(IComIIDGuidInterfaceName.Identifier.ValueText, ifaceDecl);
        });

        return true;
    }

    private bool IsFeatureAvailable(Feature feature)
    {
        return feature switch
        {
            Feature.InterfaceStaticMembers => (int)this.LanguageVersion >= 1100 && this.IsTargetFrameworkAtLeastDotNetVersion(7),
            _ => throw new NotImplementedException(),
        };
    }

    private bool TryGetTargetDotNetVersion([NotNullWhen(true)] out Version? dotNetVersion)
    {
        dotNetVersion = this.compilation?.ReferencedAssemblyNames.FirstOrDefault(id => string.Equals(id.Name, "System.Runtime", StringComparison.OrdinalIgnoreCase))?.Version;
        return dotNetVersion is not null;
    }

    private bool IsTargetFrameworkAtLeastDotNetVersion(int majorVersion)
    {
        return this.TryGetTargetDotNetVersion(out Version? actualVersion) && actualVersion.Major >= majorVersion;
    }

    private bool IsTypeDefStruct(TypeHandleInfo? typeHandleInfo)
    {
        if (typeHandleInfo is HandleTypeHandleInfo handleInfo)
        {
            if (handleInfo.Handle.Kind == HandleKind.TypeDefinition)
            {
                TypeDefinition typeDef = this.Reader.GetTypeDefinition((TypeDefinitionHandle)handleInfo.Handle);
                return this.IsTypeDefStruct(typeDef);
            }
            else if (handleInfo.Handle.Kind == HandleKind.TypeReference)
            {
                if (this.TryGetTypeDefHandle((TypeReferenceHandle)handleInfo.Handle, out TypeDefinitionHandle tdh))
                {
                    TypeDefinition typeDef = this.Reader.GetTypeDefinition(tdh);
                    return this.IsTypeDefStruct(typeDef);
                }
                else if (this.SuperGenerator is object)
                {
                    TypeReference typeReference = this.Reader.GetTypeReference((TypeReferenceHandle)handleInfo.Handle);
                    if (this.SuperGenerator.TryGetTargetGenerator(new QualifiedTypeReference(this, typeReference), out Generator? targetGenerator))
                    {
                        if (targetGenerator.TryGetTypeDefHandle(this.Reader.GetString(typeReference.Namespace), this.Reader.GetString(typeReference.Name), out TypeDefinitionHandle foreignTypeDefHandle))
                        {
                            TypeDefinition foreignTypeDef = targetGenerator.Reader.GetTypeDefinition(foreignTypeDefHandle);
                            return targetGenerator.IsTypeDefStruct(foreignTypeDef);
                        }
                    }
                }
            }
        }
        else if (SpecialTypeDefNames.Contains(null!/*TODO*/))
        {
            return true;
        }

        return false;
    }

    private bool IsDelegateReference(TypeHandleInfo typeHandleInfo, out TypeDefinition delegateTypeDef)
    {
        if (typeHandleInfo is PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo handleInfo })
        {
            return this.IsDelegateReference(handleInfo, out delegateTypeDef);
        }
        else if (typeHandleInfo is HandleTypeHandleInfo handleInfo1)
        {
            return this.IsDelegateReference(handleInfo1, out delegateTypeDef);
        }

        delegateTypeDef = default;
        return false;
    }

    private bool IsDelegateReference(HandleTypeHandleInfo typeHandleInfo, out TypeDefinition delegateTypeDef)
    {
        if (typeHandleInfo.Handle.Kind == HandleKind.TypeDefinition)
        {
            var tdh = (TypeDefinitionHandle)typeHandleInfo.Handle;
            delegateTypeDef = this.Reader.GetTypeDefinition(tdh);
            return this.IsDelegate(delegateTypeDef);
        }

        if (typeHandleInfo.Handle.Kind == HandleKind.TypeReference)
        {
            var trh = (TypeReferenceHandle)typeHandleInfo.Handle;
            if (this.TryGetTypeDefHandle(trh, out TypeDefinitionHandle tdh))
            {
                delegateTypeDef = this.Reader.GetTypeDefinition(tdh);
                return this.IsDelegate(delegateTypeDef);
            }
        }

        delegateTypeDef = default;
        return false;
    }

    private bool IsNestedType(EntityHandle typeHandle)
    {
        switch (typeHandle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition typeDef = this.Reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
                return typeDef.IsNested;
            case HandleKind.TypeReference:
                return this.TryGetTypeDefHandle((TypeReferenceHandle)typeHandle, out TypeDefinitionHandle typeDefHandle) && this.IsNestedType(typeDefHandle);
        }

        return false;
    }

    private bool IsManagedType(TypeDefinitionHandle typeDefinitionHandle)
    {
        if (this.managedTypesCheck.TryGetValue(typeDefinitionHandle, out bool result))
        {
            return result;
        }

        HashSet<TypeDefinitionHandle> visitedTypes = new();
        Dictionary<TypeDefinitionHandle, List<TypeDefinitionHandle>>? cycleFixups = null;
        result = Helper(typeDefinitionHandle)!.Value;

        // Dependency cycles may have prevented detection of managed types. Such may be managed if any in the cycle were ultimately deemed to be managed.
        if (cycleFixups?.Count > 0)
        {
            foreach (var fixup in cycleFixups)
            {
                if (this.managedTypesCheck[fixup.Key])
                {
                    foreach (TypeDefinitionHandle dependent in fixup.Value)
                    {
                        this.managedTypesCheck[dependent] = true;
                    }
                }
            }

            // This may have changed the result we are to return, so look up the current answer.
            result = this.managedTypesCheck[typeDefinitionHandle];
        }

        return result;

        bool? Helper(TypeDefinitionHandle typeDefinitionHandle)
        {
            if (this.managedTypesCheck.TryGetValue(typeDefinitionHandle, out bool result))
            {
                return result;
            }

            if (!visitedTypes.Add(typeDefinitionHandle))
            {
                // Avoid recursion. We just don't know the answer yet.
                return null;
            }

            TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefinitionHandle);
            try
            {
                if ((typeDef.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
                {
                    result = this.options.AllowMarshaling && !this.IsNonCOMInterface(typeDef);
                    this.managedTypesCheck.Add(typeDefinitionHandle, result);
                    return result;
                }

                if ((typeDef.Attributes & TypeAttributes.Class) == TypeAttributes.Class && this.Reader.StringComparer.Equals(typeDef.Name, "Apis"))
                {
                    // We arguably should never be asked about this class, which is never generated.
                    this.managedTypesCheck.Add(typeDefinitionHandle, false);
                    return false;
                }

                this.GetBaseTypeInfo(typeDef, out StringHandle baseName, out StringHandle baseNamespace);
                if (this.Reader.StringComparer.Equals(baseName, nameof(ValueType)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    if (this.IsTypeDefStruct(typeDef))
                    {
                        this.managedTypesCheck.Add(typeDefinitionHandle, false);
                        return false;
                    }
                    else
                    {
                        foreach (FieldDefinitionHandle fieldHandle in typeDef.GetFields())
                        {
                            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldHandle);
                            try
                            {
                                TypeHandleInfo fieldType = fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null);
                                TypeHandleInfo elementType = fieldType;
                                while (elementType is ITypeHandleContainer container)
                                {
                                    elementType = container.ElementType;
                                }

                                if (elementType is PrimitiveTypeHandleInfo)
                                {
                                    // These are never managed.
                                    continue;
                                }
                                else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeDefinition } fieldTypeDefHandle })
                                {
                                    if (TestFieldAndHandleCycle((TypeDefinitionHandle)fieldTypeDefHandle) is true)
                                    {
                                        return true;
                                    }
                                }
                                else if (elementType is HandleTypeHandleInfo { Handle: { Kind: HandleKind.TypeReference } fieldTypeRefHandle })
                                {
                                    if (this.TryGetTypeDefHandle((TypeReferenceHandle)fieldTypeRefHandle, out TypeDefinitionHandle tdr) && TestFieldAndHandleCycle(tdr) is true)
                                    {
                                        return true;
                                    }
                                }
                                else
                                {
                                    throw new GenerationFailedException("Unrecognized type.");
                                }

                                bool? TestFieldAndHandleCycle(TypeDefinitionHandle tdh)
                                {
                                    bool? result = Helper(tdh);
                                    switch (result)
                                    {
                                        case true:
                                            this.managedTypesCheck.Add(typeDefinitionHandle, true);
                                            break;
                                        case null:
                                            cycleFixups ??= new();
                                            if (!cycleFixups.TryGetValue(tdh, out var list))
                                            {
                                                cycleFixups.Add(tdh, list = new());
                                            }

                                            list.Add(typeDefinitionHandle);
                                            break;
                                    }

                                    return result;
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new GenerationFailedException($"Unable to ascertain whether the {this.Reader.GetString(fieldDef.Name)} field represents a managed type.", ex);
                            }
                        }

                        this.managedTypesCheck.Add(typeDefinitionHandle, false);
                        return false;
                    }
                }
                else if (this.Reader.StringComparer.Equals(baseName, nameof(Enum)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    this.managedTypesCheck.Add(typeDefinitionHandle, false);
                    return false;
                }
                else if (this.Reader.StringComparer.Equals(baseName, nameof(MulticastDelegate)) && this.Reader.StringComparer.Equals(baseNamespace, nameof(System)))
                {
                    // Delegates appear as unmanaged function pointers when using structs instead of COM interfaces.
                    // But certain delegates are never declared as delegates.
                    result = this.options.AllowMarshaling && !this.IsUntypedDelegate(typeDef);
                    this.managedTypesCheck.Add(typeDefinitionHandle, result);
                    return result;
                }

                throw new NotSupportedException();
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException($"Unable to determine if {new HandleTypeHandleInfo(this.Reader, typeDefinitionHandle).ToTypeSyntax(this.errorMessageTypeSettings, null)} is a managed type.", ex);
            }
        }
    }

    private UnmanagedType? GetUnmanagedType(BlobHandle blobHandle)
    {
        if (blobHandle.IsNil)
        {
            return null;
        }

        BlobReader br = this.Reader.GetBlobReader(blobHandle);
        var unmgdType = (UnmanagedType)br.ReadByte();
        return unmgdType;
    }

    private ExpressionSyntax ToExpressionSyntax(Constant constant)
    {
        BlobReader blobReader = this.Reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blobReader.ReadBoolean() ? LiteralExpression(SyntaxKind.TrueLiteralExpression) : LiteralExpression(SyntaxKind.FalseLiteralExpression),
            ConstantTypeCode.Char => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadChar())),
            ConstantTypeCode.SByte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadSByte())),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadByte())),
            ConstantTypeCode.Int16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt16())),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt16())),
            ConstantTypeCode.Int32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt32())),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt32())),
            ConstantTypeCode.Int64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt64())),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt64())),
            ConstantTypeCode.Single => FloatExpression(blobReader.ReadSingle()),
            ConstantTypeCode.Double => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadDouble())),
            ConstantTypeCode.String => blobReader.ReadConstant(constant.TypeCode) is string value ? LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value)) : LiteralExpression(SyntaxKind.NullLiteralExpression),
            ConstantTypeCode.NullReference => LiteralExpression(SyntaxKind.NullLiteralExpression),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        static ExpressionSyntax FloatExpression(float value)
        {
            return
                float.IsPositiveInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.PositiveInfinity))) :
                float.IsNegativeInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.NegativeInfinity))) :
                float.IsNaN(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), IdentifierName(nameof(float.NaN))) :
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value));
        }
    }

    private ExpressionSyntax ToHexExpressionSyntax(Constant constant, bool assignableToSignedInteger)
    {
        BlobReader blobReader = this.Reader.GetBlobReader(constant.Value);
        BlobReader blobReader2 = this.Reader.GetBlobReader(constant.Value);
        BlobReader blobReader3 = this.Reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.SByte => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadSByte()), blobReader2.ReadSByte())), SyntaxKind.SByteKeyword),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadByte()), blobReader2.ReadByte())),
            ConstantTypeCode.Int16 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt16()), blobReader2.ReadInt16())), SyntaxKind.ShortKeyword),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt16()), blobReader2.ReadUInt16())),
            ConstantTypeCode.Int32 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt32()), blobReader2.ReadInt32())), SyntaxKind.IntKeyword),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt32()), blobReader2.ReadUInt32())),
            ConstantTypeCode.Int64 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt64()), blobReader2.ReadInt64())), SyntaxKind.LongKeyword),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt64()), blobReader2.ReadUInt64())),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        ExpressionSyntax UncheckedSignedWrapper(LiteralExpressionSyntax value, SyntaxKind signedType)
        {
            return assignableToSignedInteger && value.Token.Text.StartsWith("0xF", StringComparison.OrdinalIgnoreCase)
                ? UncheckedExpression(CastExpression(PredefinedType(Token(signedType)), value))
                : value;
        }
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
