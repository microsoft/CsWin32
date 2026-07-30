// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <content>
/// Contains the generation of the <c>ComOutPtrMarshalling</c> policy, its shared projection helpers,
/// and the raw ABI companions the policy-bearing friendly overloads invoke.
/// </content>
public partial class Generator
{
    /// <summary>The simple name of the generated policy enum.</summary>
    private const string ComOutPtrMarshallingEnumName = "ComOutPtrMarshalling";

    /// <summary>The simple name of the generated class that resolves the policy and projects raw pointers.</summary>
    private const string ComOutPtrHelpersClassName = "ComOutPtrHelpers";

    /// <summary>
    /// The suffix appended to a native method or interface name to produce its raw ABI companion.
    /// </summary>
    private const string ComOutPtrRawSuffix = "__ComOutPtrRaw";

    private static readonly TypeSyntax NIntTypeSyntax = IdentifierName("nint");

    /// <summary>The name of the policy parameter appended to the policy-bearing friendly overload.</summary>
    private static readonly IdentifierNameSyntax ComOutPtrPolicyParameterName = IdentifierName("marshalling");

    /// <summary>The name of the local that holds the resolved policy for the duration of the call.</summary>
    private static readonly IdentifierNameSyntax ComOutPtrPolicyLocalName = IdentifierName("__marshalling");

    /// <summary>The name of the local that receives the raw ABI pointer from native code.</summary>
    private static readonly IdentifierNameSyntax ComOutPtrNativeLocalName = IdentifierName("__ppv");

    /// <summary>Preserves the fields C#/WinRT reflects over when it generates an interface identifier.</summary>
    private static readonly AttributeSyntax DynamicallyAccessedPublicFieldsAttributeSyntax = Attribute(IdentifierName("DynamicallyAccessedMembers"))
        .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName("DynamicallyAccessedMemberTypes"),
            IdentifierName("PublicFields"))));

    /// <summary>
    /// Gets a value indicating whether friendly overloads should carry a <c>ComOutPtrMarshalling</c> parameter.
    /// </summary>
    /// <remarks>
    /// The policy is only meaningful when the projection is produced by the COM source generators, because the
    /// wrapper families it selects between (<c>ComInterfaceMarshaller</c>, <c>UniqueComInterfaceMarshaller</c> and
    /// the C#/WinRT marshallers) are only available there.
    /// </remarks>
    private bool EmitComOutPtrMarshallingPolicy => this.useSourceGenerators && this.options.FriendlyOverloads.ComOutPtrGenericOverloads;

    /// <summary>
    /// Gets the fully qualified name of the generated <c>ComOutPtrMarshalling</c> enum.
    /// </summary>
    private NameSyntax ComOutPtrMarshallingTypeSyntax => ParseName($"global::{this.MainGenerator.Namespace}.{ComOutPtrMarshallingEnumName}");

    /// <summary>
    /// Gets the fully qualified name of the generated projection helper class.
    /// </summary>
    private NameSyntax ComOutPtrHelpersTypeSyntax => ParseName($"global::{this.MainGenerator.Namespace}.{ComOutPtrHelpersClassName}");

    /// <summary>
    /// Reverses the visibility elevation that template fetching applies to the outermost declaration.
    /// </summary>
    /// <param name="member">The fetched template member.</param>
    /// <returns>The member with an <see langword="internal"/> outermost declaration.</returns>
    private static MemberDeclarationSyntax DemoteToInternal(MemberDeclarationSyntax member)
    {
        int indexOfPublic = member.Modifiers.IndexOf(SyntaxKind.PublicKeyword);
        return indexOfPublic < 0
            ? member
            : member.WithModifiers(member.Modifiers.Replace(member.Modifiers[indexOfPublic], TokenWithSpace(SyntaxKind.InternalKeyword)));
    }

    /// <summary>
    /// Applies the <c>GeneratedCode</c> attribute without displacing any documentation comment that precedes the declaration.
    /// </summary>
    /// <param name="member">The declaration to annotate.</param>
    /// <returns>The annotated declaration.</returns>
    private static MemberDeclarationSyntax AddGeneratedCodeAttributeBeforeDocs(MemberDeclarationSyntax member) =>
        member
            .WithoutLeadingTrivia()
            .AddAttributeLists(AttributeList(GeneratedCodeAttribute))
            .WithLeadingTrivia(member.GetLeadingTrivia());

    /// <summary>
    /// Rewrites a COM output pointer parameter so it carries the raw ABI pointer instead of a managed wrapper.
    /// </summary>
    /// <param name="parameter">The <c>void**</c> or <c>out object</c> parameter to rewrite.</param>
    /// <returns>An <c>out nint</c> parameter with the same name.</returns>
    private static ParameterSyntax AsRawComOutPtrParameter(ParameterSyntax parameter) =>
        Parameter(NIntTypeSyntax.WithTrailingTrivia(TriviaList(Space)), parameter.Identifier)
            .WithModifiers([TokenWithSpace(SyntaxKind.OutKeyword)]);

    /// <summary>
    /// Locates the canonical <c>IID_PPV_ARGS</c> pair on a method: a <c>Guid*</c> parameter immediately followed by
    /// a <c>void**</c> parameter carrying <c>[ComOutPtr]</c>, positioned as the final two parameters.
    /// </summary>
    /// <param name="methodDefinition">The method to scan.</param>
    /// <param name="signature">The decoded signature of <paramref name="methodDefinition"/>.</param>
    /// <param name="riidIndex">Receives the zero-based parameter index of the <c>Guid*</c>.</param>
    /// <param name="ppvIndex">Receives the zero-based parameter index of the <c>void**</c>.</param>
    /// <returns><see langword="true"/> when the pattern was found.</returns>
    private bool TryFindComOutPtrPair(MethodDefinition methodDefinition, MethodSignature<TypeHandleInfo> signature, out int riidIndex, out int ppvIndex)
    {
        riidIndex = -1;
        ppvIndex = -1;

        List<Parameter> metadataParams = new();
        foreach (ParameterHandle ph in methodDefinition.GetParameters())
        {
            Parameter p = this.Reader.GetParameter(ph);
            if (p.SequenceNumber > 0 && p.SequenceNumber - 1 < signature.ParameterTypes.Length)
            {
                metadataParams.Add(p);
            }
        }

        if (metadataParams.Count < 2)
        {
            return false;
        }

        // Only match when the pair are the final two parameters (the canonical IID_PPV_ARGS position).
        Parameter riidParam = metadataParams[metadataParams.Count - 2];
        Parameter ppvParam = metadataParams[metadataParams.Count - 1];
        int riid = riidParam.SequenceNumber - 1;
        int ppv = ppvParam.SequenceNumber - 1;

        if (ppv != riid + 1
            || signature.ParameterTypes[riid] is not PointerTypeHandleInfo { ElementType: HandleTypeHandleInfo guidInfo }
            || !guidInfo.IsType("Guid")
            || this.FindInteropDecorativeAttribute(ppvParam.GetCustomAttributes(), "ComOutPtrAttribute") is null
            || signature.ParameterTypes[ppv] is not PointerTypeHandleInfo { ElementType: PointerTypeHandleInfo { ElementType: PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Void } } })
        {
            return false;
        }

        riidIndex = riid;
        ppvIndex = ppv;
        return true;
    }

    /// <summary>
    /// Locates the <c>IID_PPV_ARGS</c> pair that a friendly overload should replace with a generic type parameter.
    /// </summary>
    /// <param name="methodDefinition">The method to scan.</param>
    /// <param name="signature">The decoded signature of <paramref name="methodDefinition"/>.</param>
    /// <param name="externMethodDeclaration">The generated declaration the friendly overload will call.</param>
    /// <param name="riidIndex">Receives the zero-based parameter index of the <c>Guid*</c>.</param>
    /// <param name="ppvIndex">Receives the zero-based parameter index of the COM output pointer.</param>
    /// <param name="ppvIsObjectOut">Receives a value indicating whether the generated output parameter is already an <c>out object</c>.</param>
    /// <returns><see langword="true"/> when a generic friendly overload should be generated.</returns>
    private bool TryFindComOutPtrFriendlyPair(
        MethodDefinition methodDefinition,
        MethodSignature<TypeHandleInfo> signature,
        MethodDeclarationSyntax externMethodDeclaration,
        out int riidIndex,
        out int ppvIndex,
        out bool ppvIsObjectOut)
    {
        ppvIsObjectOut = false;
        if (!this.options.FriendlyOverloads.ComOutPtrGenericOverloads
            || !this.TryFindComOutPtrPair(methodDefinition, signature, out riidIndex, out ppvIndex)
            || riidIndex >= externMethodDeclaration.ParameterList.Parameters.Count
            || ppvIndex >= externMethodDeclaration.ParameterList.Parameters.Count)
        {
            riidIndex = -1;
            ppvIndex = -1;
            return false;
        }

        ParameterSyntax ppvExtern = externMethodDeclaration.ParameterList.Parameters[ppvIndex];
        if (ppvExtern.Type is IdentifierNameSyntax { Identifier.ValueText: nameof(IntPtr) })
        {
            // UseIntPtrForComOutPointers mode already gives the caller the raw pointer.
            riidIndex = -1;
            ppvIndex = -1;
            return false;
        }

        ppvIsObjectOut = ppvExtern.Modifiers.Any(SyntaxKind.OutKeyword)
            && ppvExtern.Type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ObjectKeyword };
        return true;
    }

    /// <summary>
    /// Emits the <c>ComOutPtrMarshalling</c> enum and the projection helper class exactly once.
    /// </summary>
    private void RequestComOutPtrMarshallingPolicy()
    {
        // Always generate these in the context of the most common metadata so we don't emit them more than once.
        if (!this.IsWin32Sdk)
        {
            this.MainGenerator.volatileCode.GenerationTransaction(() => this.MainGenerator.RequestComOutPtrMarshallingPolicy());
            return;
        }

        this.volatileCode.GenerateSpecialType(ComOutPtrMarshallingEnumName, delegate
        {
            if (!TryFetchTemplate(ComOutPtrMarshallingEnumName, this, out MemberDeclarationSyntax? enumDeclaration))
            {
                throw new GenerationFailedException($"Failed to retrieve template: {ComOutPtrMarshallingEnumName}");
            }

            this.volatileCode.AddSpecialType(ComOutPtrMarshallingEnumName, AddGeneratedCodeAttributeBeforeDocs(enumDeclaration));
        });

        this.volatileCode.GenerateSpecialType(ComOutPtrHelpersClassName, delegate
        {
            if (!TryFetchTemplate(ComOutPtrHelpersClassName, this, out MemberDeclarationSyntax? helpersDeclaration))
            {
                throw new GenerationFailedException($"Failed to retrieve template: {ComOutPtrHelpersClassName}");
            }

            // The helpers are an implementation detail of the generated overloads, so they never become public.
            this.volatileCode.AddSpecialType(ComOutPtrHelpersClassName, AddGeneratedCodeAttributeBeforeDocs(DemoteToInternal(helpersDeclaration)));
        });
    }

    /// <summary>
    /// Declares an internal COM interface that shares the IID and vtable layout of <paramref name="publicInterfaceName"/>
    /// but exposes the COM output pointer of the marked methods as <c>out nint</c>.
    /// </summary>
    /// <param name="publicInterfaceName">The simple name of the interface being mirrored.</param>
    /// <param name="interfaceNamespace">The full metadata namespace that declares the interface.</param>
    /// <param name="attributes">The <c>[Guid]</c>, <c>[InterfaceType]</c> and <c>[GeneratedComInterface]</c> attributes copied from the public interface.</param>
    /// <param name="baseType">The base interface, when it contributes vtable slots that <paramref name="methods"/> does not enumerate.</param>
    /// <param name="methods">Every method in vtable order, paired with the index of the COM output pointer to expose raw (or -1).</param>
    private void DeclareComOutPtrRawInterface(
        string publicInterfaceName,
        string interfaceNamespace,
        AttributeListSyntax attributes,
        BaseTypeSyntax? baseType,
        IReadOnlyList<(MethodDeclarationSyntax Method, int RawOutParameterIndex)> methods)
    {
        string rawName = publicInterfaceName + ComOutPtrRawSuffix;
        string specialTypeName = $"{interfaceNamespace}.{rawName}";
        this.volatileCode.GenerateSpecialType(specialTypeName, delegate
        {
            List<MemberDeclarationSyntax> members = new(methods.Count);
            foreach ((MethodDeclarationSyntax method, int rawOutParameterIndex) in methods)
            {
                MethodDeclarationSyntax raw = method.WithLeadingTrivia();
                int indexOfNew = raw.Modifiers.IndexOf(SyntaxKind.NewKeyword);
                if (indexOfNew >= 0)
                {
                    raw = raw.WithModifiers(raw.Modifiers.RemoveAt(indexOfNew));
                }

                if (rawOutParameterIndex >= 0)
                {
                    ParameterSyntax ppv = raw.ParameterList.Parameters[rawOutParameterIndex];
                    raw = raw.WithParameterList(FixTrivia(raw.ParameterList.WithParameters(
                        raw.ParameterList.Parameters.Replace(ppv, AsRawComOutPtrParameter(ppv)))));
                }

                members.Add(raw);
            }

            InterfaceDeclarationSyntax rawInterface = InterfaceDeclaration(Identifier(rawName), [.. members])
                .WithKeyword(TokenWithSpace(SyntaxKind.InterfaceKeyword))
                .AddModifiers(TokenWithSpace(SyntaxKind.InternalKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.PartialKeyword))
                .AddAttributeLists(attributes, AttributeList(GeneratedCodeAttribute))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>Shares the IID and vtable layout of <see cref=\"{rawName.Substring(0, rawName.Length - ComOutPtrRawSuffix.Length)}\"/> but receives COM output pointers as raw ABI pointers so the caller may choose the managed projection.</summary>\n"));

            if (baseType is not null)
            {
                rawInterface = rawInterface.WithBaseList(BaseList(baseType));
            }

            MemberDeclarationSyntax declaration = rawInterface;
            if (this.TryStripCommonNamespace(interfaceNamespace, out string? shortNamespace) && shortNamespace.Length > 0)
            {
                declaration = declaration.WithAdditionalAnnotations(new SyntaxAnnotation(NamespaceContainerAnnotation, shortNamespace));
            }

            this.volatileCode.AddSpecialType(specialTypeName, declaration);
        });
    }

    /// <summary>
    /// Declares a private <c>[LibraryImport]</c> companion that shares the native entry point of
    /// <paramref name="externMethodDeclaration"/> but exposes its COM output pointer as <c>out nint</c>.
    /// </summary>
    /// <param name="externMethodDeclaration">The public generated p/invoke declaration.</param>
    /// <param name="ppvIndex">The zero-based index of the COM output pointer parameter.</param>
    /// <returns>The companion declaration, or <see langword="null"/> when the public declaration is not a direct <c>[LibraryImport]</c>.</returns>
    private MethodDeclarationSyntax? DeclareComOutPtrRawExternMethod(MethodDeclarationSyntax externMethodDeclaration, int ppvIndex)
    {
        AttributeSyntax? libraryImport = externMethodDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "LibraryImport");
        if (libraryImport is null)
        {
            return null;
        }

        // The entry point is implicit in the method name unless the attribute already names it.
        if (libraryImport.ArgumentList?.Arguments.Any(a => a.NameEquals?.Name.Identifier.ValueText == "EntryPoint") is not true)
        {
            AttributeArgumentSyntax entryPoint = AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(externMethodDeclaration.Identifier.ValueText)))
                .WithNameEquals(NameEquals(IdentifierName("EntryPoint")));
            AttributeSyntax withEntryPoint = libraryImport.AddArgumentListArguments(entryPoint);
            externMethodDeclaration = externMethodDeclaration.ReplaceNode(libraryImport, withEntryPoint);
        }

        ParameterSyntax ppv = externMethodDeclaration.ParameterList.Parameters[ppvIndex];
        return externMethodDeclaration
            .WithLeadingTrivia()
            .WithIdentifier(Identifier(externMethodDeclaration.Identifier.ValueText + ComOutPtrRawSuffix))
            .WithModifiers([TokenWithSpace(SyntaxKind.PrivateKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)])
            .WithParameterList(FixTrivia(externMethodDeclaration.ParameterList.WithParameters(
                externMethodDeclaration.ParameterList.Parameters.Replace(ppv, AsRawComOutPtrParameter(ppv)))));
    }

    /// <summary>
    /// Derives the compatibility overload from a policy-bearing overload by dropping the trailing policy parameter
    /// and forwarding to the policy overload with <c>ComOutPtrMarshalling.Default</c>.
    /// </summary>
    /// <param name="policyOverload">The generated policy-bearing overload.</param>
    /// <param name="addOverloadResolutionPriority"><see langword="true"/> to apply <c>[OverloadResolutionPriority(1)]</c> to the result.</param>
    /// <returns>The compatibility overload, whose signature matches what CsWin32 generated before the policy existed.</returns>
    private MethodDeclarationSyntax DeclareComOutPtrCompatibilityOverload(MethodDeclarationSyntax policyOverload, bool addOverloadResolutionPriority)
    {
        SeparatedSyntaxList<ParameterSyntax> policyParameters = policyOverload.ParameterList.Parameters;
        SeparatedSyntaxList<ParameterSyntax> compatParameters = policyParameters.RemoveAt(policyParameters.Count - 1);

        List<ArgumentSyntax> arguments = new(compatParameters.Count + 1);
        foreach (ParameterSyntax parameter in compatParameters)
        {
            arguments.Add(Argument(IdentifierName(parameter.Identifier.Text))
                .WithRefKindKeyword(parameter.Modifiers.FirstOrDefault(m => m.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword)));
        }

        arguments.Add(Argument(MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            this.ComOutPtrMarshallingTypeSyntax,
            IdentifierName("Default"))));

        // The forwarding call is unqualified so it binds to the sibling overload regardless of how the host class is shaped.
        ExpressionSyntax invocation = InvocationExpression(
            GenericName(policyOverload.Identifier.ValueText, TypeArgumentList(IdentifierName("T"))),
            [.. arguments]);

        bool hasVoidReturn = policyOverload.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        BlockSyntax body = Block(hasVoidReturn ? ExpressionStatement(invocation) : ReturnStatement(invocation))
            .WithOpenBraceToken(Token(TriviaList(LineFeed), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
            .WithCloseBraceToken(TokenWithLineFeed(SyntaxKind.CloseBraceToken));

        MethodDeclarationSyntax compatOverload = policyOverload
            .WithParameterList(FixTrivia(ParameterList(compatParameters)))
            .WithBody(body);

        if (addOverloadResolutionPriority)
        {
            this.volatileCode.GenerationTransaction(() => this.DeclareOverloadResolutionPriorityAttributeIfNecessary());
            compatOverload = compatOverload
                .WithoutLeadingTrivia()
                .AddAttributeLists(AttributeList(OverloadResolutionPriorityAttribute(1)))
                .WithLeadingTrivia(policyOverload.GetLeadingTrivia());
        }

        return compatOverload;
    }

    /// <summary>
    /// Identifies the raw ABI companion that a policy-bearing friendly overload invokes.
    /// </summary>
    /// <param name="methodName">The name of the companion method.</param>
    /// <param name="interfaceCastType">The companion interface the receiver must be cast to, or <see langword="null"/> for a flat p/invoke.</param>
    private sealed class ComOutPtrRawTarget(SimpleNameSyntax methodName, NameSyntax? interfaceCastType)
    {
        internal SimpleNameSyntax MethodName { get; } = methodName;

        internal NameSyntax? InterfaceCastType { get; } = interfaceCastType;
    }
}
