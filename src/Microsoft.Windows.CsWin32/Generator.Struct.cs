// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
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
                    TypeSyntaxSettings thisFieldTypeSettings = typeSettings;

                    // Do not qualify names of a type nested inside *this* struct, since this struct may or may not have a mangled name.
                    if (thisFieldTypeSettings.QualifyNames && fieldTypeInfo is HandleTypeHandleInfo fieldHandleTypeInfo && this.IsNestedType(fieldHandleTypeInfo.Handle))
                    {
                        if (fieldHandleTypeInfo.Handle.Kind == HandleKind.TypeReference)
                        {
                            if (this.TryGetTypeDefHandle((TypeReferenceHandle)fieldHandleTypeInfo.Handle, out QualifiedTypeDefinitionHandle fieldTypeDefHandle) && fieldTypeDefHandle.Generator == this)
                            {
                                foreach (TypeDefinitionHandle nestedTypeHandle in typeDef.GetNestedTypes())
                                {
                                    if (fieldTypeDefHandle.DefinitionHandle == nestedTypeHandle)
                                    {
                                        thisFieldTypeSettings = thisFieldTypeSettings with { QualifyNames = false };
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    TypeSyntaxAndMarshaling fieldTypeSyntax = fieldTypeInfo.ToTypeSyntax(thisFieldTypeSettings, GeneratingElement.StructMember, fieldAttributes);
                    (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) fieldInfo = this.ReinterpretFieldType(fieldDef, fieldTypeSyntax.Type, fieldAttributes, context);
                    additionalMembers = additionalMembers.AddRange(fieldInfo.AdditionalMembers);

                    PropertyDeclarationSyntax? property = null;
                    if (this.FindAssociatedEnum(fieldAttributes) is { } propertyType)
                    {
                        // Keep the field with its original type, but then add a property that returns the enum type.
                        fieldDeclarator = VariableDeclarator(SafeIdentifier($"_{fieldName}"));
                        field = FieldDeclaration(VariableDeclaration(fieldInfo.FieldType).AddVariables(fieldDeclarator))
                            .AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword));

                        // internal EnumType FieldName {
                        //    get => (EnumType)this._fieldName;
                        //    set => this._fieldName = (UnderlyingType)value;
                        // }
                        this.RequestInteropType(this.GetNamespaceForPossiblyNestedType(typeDef), propertyType.Identifier.ValueText, context);
                        ExpressionSyntax fieldAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(fieldDeclarator.Identifier));
                        property = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), Identifier(fieldName).WithTrailingTrivia(LineFeed))
                            .AddModifiers(TokenWithSpace(this.Visibility))
                            .WithAccessorList(AccessorList().AddAccessors(
                                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithExpressionBody(ArrowExpressionClause(CastExpression(propertyType, fieldAccess))).WithSemicolonToken(Semicolon),
                                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithExpressionBody(ArrowExpressionClause(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, fieldAccess, CastExpression(fieldInfo.FieldType, IdentifierName("value"))))).WithSemicolonToken(Semicolon)));
                        additionalMembers = additionalMembers.Add(property);
                    }
                    else
                    {
                        field = FieldDeclaration(VariableDeclaration(fieldInfo.FieldType).AddVariables(fieldDeclarator))
                            .AddModifiers(TokenWithSpace(this.Visibility));
                    }

                    if (fieldInfo.MarshalAsAttribute is object)
                    {
                        field = field.AddAttributeLists(AttributeList().AddAttributes(fieldInfo.MarshalAsAttribute));
                    }

                    if (this.HasObsoleteAttribute(fieldDef.GetCustomAttributes()))
                    {
                        field = field.AddAttributeLists(AttributeList().AddAttributes(ObsoleteAttributeSyntax));
                        property = property?.AddAttributeLists(AttributeList().AddAttributes(ObsoleteAttributeSyntax));
                    }

                    if (RequiresUnsafe(fieldInfo.FieldType))
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
        if ((!context.AllowMarshaling) && this.IsDelegateReference(fieldTypeHandleInfo, out QualifiedTypeDefinition typeDef) && !typeDef.Generator.IsUntypedDelegate(typeDef.Definition))
        {
            return (this.FunctionPointer(typeDef), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        // If the field is a pointer to a COM interface (and we're using bona fide interfaces),
        // then we must type it as an array.
        if (context.AllowMarshaling && fieldTypeHandleInfo is PointerTypeHandleInfo ptr3 && this.IsInterface(ptr3.ElementType))
        {
            return (ArrayType(ptr3.ElementType.ToTypeSyntax(typeSettings, GeneratingElement.Field, null).Type).AddRankSpecifiers(ArrayRankSpecifier()), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        return (originalType, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
    }
}
