// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <content>
/// Contains generation support for automatic COM and Windows Runtime object output projection.
/// </content>
public partial class Generator
{
    /// <summary>The generated adaptive custom marshaller and built-in COM projection helper.</summary>
    private const string ComOrWinRTObjectMarshallerClassName = "ComOrWinRTObjectMarshaller";

    /// <summary>Preserves the fields C#/WinRT reflects over when it generates an interface identifier.</summary>
    private static readonly AttributeSyntax DynamicallyAccessedPublicFieldsAttributeSyntax = Attribute(IdentifierName("DynamicallyAccessedMembers"))
        .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName("DynamicallyAccessedMemberTypes"),
            IdentifierName("PublicFields"))));

    /// <summary>
    /// Gets a value indicating whether recognized COM object outputs should be projected adaptively.
    /// </summary>
    private bool UseAutoWinRTMarshalling =>
        this.options.AllowMarshaling &&
        this.options.ComInterop.AutoWinRTMarshalling &&
        this.canUseCsWinRT &&
        (!this.useSourceGenerators || this.canUseCustomMarshaller);

    /// <summary>Gets the fully qualified name of the generated adaptive marshaller.</summary>
    private NameSyntax ComOrWinRTObjectMarshallerTypeSyntax =>
        ParseName($"global::{this.MainGenerator.Namespace}.{ComOrWinRTObjectMarshallerClassName}");

    /// <summary>Gets the fully qualified name of the generated IID context marshaller.</summary>
    private NameSyntax ComOutPtrIidMarshallerTypeSyntax =>
        ParseName($"global::{this.MainGenerator.Namespace}.{ComOrWinRTObjectMarshallerClassName}.IidMarshaller");

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
            riidIndex = -1;
            ppvIndex = -1;
            return false;
        }

        ppvIsObjectOut = ppvExtern.Modifiers.Any(SyntaxKind.OutKeyword)
            && ppvExtern.Type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ObjectKeyword };
        return true;
    }

    /// <summary>
    /// Applies the adaptive custom marshaller to a source-generated interop declaration when its final parameters
    /// follow the canonical IID/output-pointer pattern.
    /// </summary>
    /// <param name="methodDefinition">The metadata method represented by <paramref name="methodDeclaration"/>.</param>
    /// <param name="signature">The decoded metadata signature.</param>
    /// <param name="methodDeclaration">The generated P/Invoke or COM method declaration.</param>
    /// <param name="marshalManagedImplementerOutput">
    /// A value indicating whether the declaration can dispatch to a managed implementation, which requires carrying
    /// the requested IID through generated COM marshalling.
    /// </param>
    /// <returns>The declaration with adaptive marshalling applied when appropriate.</returns>
    private MethodDeclarationSyntax ApplyAutoWinRTMarshalling(
        MethodDefinition methodDefinition,
        MethodSignature<TypeHandleInfo> signature,
        MethodDeclarationSyntax methodDeclaration,
        bool marshalManagedImplementerOutput)
    {
        if (!this.UseAutoWinRTMarshalling
            || !this.useSourceGenerators
            || !this.TryFindComOutPtrPair(methodDefinition, signature, out int riidIndex, out int ppvIndex)
            || ppvIndex >= methodDeclaration.ParameterList.Parameters.Count)
        {
            return methodDeclaration;
        }

        ParameterSyntax ppv = methodDeclaration.ParameterList.Parameters[ppvIndex];
        if (!ppv.Modifiers.Any(SyntaxKind.OutKeyword)
            || ppv.Type is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ObjectKeyword })
        {
            return methodDeclaration;
        }

        SeparatedSyntaxList<ParameterSyntax> parameters = methodDeclaration.ParameterList.Parameters;
        if (marshalManagedImplementerOutput)
        {
            if (riidIndex >= parameters.Count || parameters[riidIndex].Type is not TypeSyntax riidType)
            {
                return methodDeclaration;
            }

            TypeSyntax iidType;
            SyntaxTokenList iidModifiers;
            if (riidType is PointerTypeSyntax { ElementType: TypeSyntax elementType })
            {
                iidType = elementType;
                iidModifiers = [TokenWithSpace(SyntaxKind.InKeyword)];
            }
            else if (parameters[riidIndex].Modifiers.Any(SyntaxKind.InKeyword))
            {
                iidType = riidType;
                iidModifiers = parameters[riidIndex].Modifiers;
            }
            else
            {
                return methodDeclaration;
            }

            AttributeSyntax marshalUsingIid = Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalUsing"))
                .AddArgumentListArguments(AttributeArgument(TypeOfExpression(this.ComOutPtrIidMarshallerTypeSyntax)));
            ParameterSyntax riid = parameters[riidIndex]
                .WithType(iidType.WithTrailingTrivia(TriviaList(Space)))
                .WithModifiers(iidModifiers)
                .AddAttributeLists(AttributeList(marshalUsingIid));
            parameters = parameters.Replace(parameters[riidIndex], riid);
        }

        SyntaxList<AttributeListSyntax> attributeLists = default;
        foreach (AttributeListSyntax attributeList in ppv.AttributeLists)
        {
            SeparatedSyntaxList<AttributeSyntax> attributes = attributeList.Attributes;
            for (int i = attributes.Count - 1; i >= 0; i--)
            {
                if (attributes[i].Name.ToString() is "MarshalAs" or "MarshalAsAttribute")
                {
                    attributes = attributes.RemoveAt(i);
                }
            }

            if (attributes.Count > 0)
            {
                attributeLists = attributeLists.Add(attributeList.WithAttributes(attributes));
            }
        }

        AttributeSyntax marshalUsing = Attribute(ParseName("global::System.Runtime.InteropServices.Marshalling.MarshalUsing"))
            .AddArgumentListArguments(AttributeArgument(TypeOfExpression(this.ComOrWinRTObjectMarshallerTypeSyntax)));
        ppv = ppv
            .WithAttributeLists(attributeLists)
            .AddAttributeLists(AttributeList(marshalUsing));
        parameters = parameters.Replace(parameters[ppvIndex], ppv);

        this.volatileCode.GenerationTransaction(this.RequestComOrWinRTObjectMarshaller);
        return methodDeclaration.WithParameterList(methodDeclaration.ParameterList.WithParameters(parameters));
    }

    /// <summary>Emits the adaptive marshaller and projection helper exactly once.</summary>
    private void RequestComOrWinRTObjectMarshaller()
    {
        if (!this.IsWin32Sdk)
        {
            this.MainGenerator.volatileCode.GenerationTransaction(() => this.MainGenerator.RequestComOrWinRTObjectMarshaller());
            return;
        }

        this.volatileCode.GenerateSpecialType(ComOrWinRTObjectMarshallerClassName, delegate
        {
            if (!TryFetchTemplate(ComOrWinRTObjectMarshallerClassName, this, out MemberDeclarationSyntax? declaration))
            {
                throw new GenerationFailedException($"Failed to retrieve template: {ComOrWinRTObjectMarshallerClassName}");
            }

            this.volatileCode.AddSpecialType(
                ComOrWinRTObjectMarshallerClassName,
                declaration
                    .WithoutLeadingTrivia()
                    .AddAttributeLists(AttributeList(GeneratedCodeAttribute))
                    .WithLeadingTrivia(declaration.GetLeadingTrivia()));
        });
    }
}
