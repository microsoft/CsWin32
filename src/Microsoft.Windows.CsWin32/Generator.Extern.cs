// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
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
}
