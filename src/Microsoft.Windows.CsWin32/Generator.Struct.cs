// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private static byte GetLengthInBytes(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte => 1,
        PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 => 8,
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr => 8, // Assume this -- guessing high isn't a problem for our use case.
        _ => throw new NotSupportedException($"Unsupported primitive type code: {code}"),
    };

    /// <summary>
    /// Gets a value indicating whether the given field name is the generated holder for an anonymous nested struct or union
    /// (e.g. <c>Anonymous</c>, <c>Anonymous1</c>, <c>Anonymous2</c>).
    /// </summary>
    private static bool IsAnonymousFieldName(string name)
    {
        const string Prefix = "Anonymous";
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = Prefix.Length; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
            {
                return false;
            }
        }

        return true;
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

        // If the last field has the [FlexibleArray] attribute, we must disable marshaling since the struct
        // is only ever valid when accessed via a pointer since the struct acts as a header of an arbitrarily-sized array.
        FieldDefinitionHandle flexibleArrayFieldHandle = default;
        MethodDeclarationSyntax? sizeOfMethod = null;
        if (typeDef.GetFields().LastOrDefault() is FieldDefinitionHandle { IsNil: false } lastFieldHandle)
        {
            FieldDefinition lastField = this.Reader.GetFieldDefinition(lastFieldHandle);
            if (MetadataUtilities.FindAttribute(this.Reader, lastField.GetCustomAttributes(), InteropDecorationNamespace, FlexibleArrayAttribute) is not null)
            {
                flexibleArrayFieldHandle = lastFieldHandle;
                context = context with { AllowMarshaling = false };
            }
        }

        TypeSyntaxSettings typeSettings = context.Filter(this.fieldTypeSettings);

        bool hasUtf16CharField = false;
        var members = new List<MemberDeclarationSyntax>();
        SyntaxList<MemberDeclarationSyntax> additionalMembers = default;
        foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            FieldDeclarationSyntax field;

            if (fieldDef.Attributes.HasFlag(FieldAttributes.Static))
            {
                if (fieldDef.Attributes.HasFlag(FieldAttributes.Literal))
                {
                    field = this.DeclareConstant(fieldDef);
                    members.Add(field);
                    continue;
                }
                else
                {
                    throw new NotSupportedException();
                }
            }

            string fieldName = this.Reader.GetString(fieldDef.Name);

            try
            {
                CustomAttribute? fixedBufferAttribute = this.FindAttribute(fieldDef.GetCustomAttributes(), SystemRuntimeCompilerServices, nameof(FixedBufferAttribute));

                VariableDeclaratorSyntax fieldDeclarator = VariableDeclarator(SafeIdentifier(fieldName));
                if (fixedBufferAttribute.HasValue)
                {
                    CustomAttributeValue<TypeSyntax> attributeArgs = fixedBufferAttribute.Value.DecodeValue(CustomAttributeTypeProvider.Instance);
                    var fieldType = (TypeSyntax)attributeArgs.FixedArguments[0].Value!;
                    ExpressionSyntax size = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)attributeArgs.FixedArguments[1].Value!));
                    field = FieldDeclaration(
                        [TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), Token(SyntaxKind.FixedKeyword)],
                        VariableDeclaration(fieldType, [fieldDeclarator.WithArgumentList(BracketedArgumentList(Argument(size)))]));
                }
                else if (fieldDefHandle == flexibleArrayFieldHandle)
                {
                    QualifiedCustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes().QualifyWith(this);
                    var fieldTypeInfo = (ArrayTypeHandleInfo)fieldDef.DecodeSignature(this.SignatureHandleProvider, null);
                    TypeSyntax fieldType = fieldTypeInfo.ElementType.ToTypeSyntax(typeSettings, GeneratingElement.StructMember, fieldAttributes).Type;

                    if (fieldType is PointerTypeSyntax or FunctionPointerTypeSyntax)
                    {
                        // These types are not allowed as generic type arguments (https://github.com/dotnet/runtime/issues/13627)
                        // so we have to generate a special nested struct dedicated to this type instead of using the generic type.
                        StructDeclarationSyntax helperStruct = this.DeclareVariableLengthInlineArrayHelper(context, fieldType);
                        additionalMembers = additionalMembers.Add(helperStruct);

                        field = FieldDeclaration([TokenWithSpace(this.Visibility)], VariableDeclaration(IdentifierName(helperStruct.Identifier.ValueText), [fieldDeclarator]));
                    }
                    else if (fieldType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.CharKeyword })
                    {
                        // If the field is a char, we need to use a helper struct to avoid marshaling issues
                        // because although C# considered char to be "unmanaged", .NET Framework considers it non-blittable.
                        this.RequestVariableLengthInlineArrayHelper2(context);
                        field = FieldDeclaration(
                            [TokenWithSpace(this.Visibility)],
                            VariableDeclaration(
                                GenericName($"global::Windows.Win32.VariableLengthInlineArray", [fieldType, PredefinedType(Token(SyntaxKind.UShortKeyword))]),
                                [fieldDeclarator]));
                    }
                    else
                    {
                        this.RequestVariableLengthInlineArrayHelper1(context);
                        field = FieldDeclaration(
                            [TokenWithSpace(this.Visibility)],
                            VariableDeclaration(
                                GenericName($"global::Windows.Win32.VariableLengthInlineArray", [fieldType]),
                                [fieldDeclarator]));
                    }

                    sizeOfMethod = this.DeclareSizeOfMethod(name, fieldType, typeSettings);
                }
                else
                {
                    CustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes();
                    TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null);
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

                    TypeSyntaxAndMarshaling fieldTypeSyntax = fieldTypeInfo.ToTypeSyntax(thisFieldTypeSettings, GeneratingElement.StructMember, fieldAttributes.QualifyWith(this));
                    (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) fieldInfo = this.ReinterpretFieldType(fieldDef, fieldTypeSyntax.Type, fieldAttributes, context);
                    additionalMembers = additionalMembers.AddRange(fieldInfo.AdditionalMembers);

                    PropertyDeclarationSyntax? property = null;
                    if (this.FindAssociatedEnum(fieldAttributes) is { } propertyType)
                    {
                        // Keep the field with its original type, but then add a property that returns the enum type.
                        fieldDeclarator = VariableDeclarator(SafeIdentifier($"_{fieldName}"));
                        field = FieldDeclaration([TokenWithSpace(SyntaxKind.PrivateKeyword)], VariableDeclaration(fieldInfo.FieldType, [fieldDeclarator]));

                        // internal EnumType FieldName {
                        //    get => (EnumType)this._fieldName;
                        //    set => this._fieldName = (UnderlyingType)value;
                        // }
                        this.RequestInteropType(this.GetNamespaceForPossiblyNestedType(typeDef), propertyType.Identifier.ValueText, context);
                        ExpressionSyntax fieldAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(fieldDeclarator.Identifier));
                        property = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), Identifier(fieldName).WithTrailingTrivia(LineFeed))
                            .AddModifiers(TokenWithSpace(this.Visibility))
                            .WithAccessorList(AccessorList(
                                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithExpressionBody(ArrowExpressionClause(CastExpression(propertyType, fieldAccess))).WithSemicolonToken(Semicolon),
                                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithExpressionBody(ArrowExpressionClause(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, fieldAccess, CastExpression(fieldInfo.FieldType, IdentifierName("value"))))).WithSemicolonToken(Semicolon)));
                        additionalMembers = additionalMembers.Add(property);
                    }
                    else
                    {
                        field = FieldDeclaration([TokenWithSpace(this.Visibility)], VariableDeclaration(fieldInfo.FieldType, [fieldDeclarator]));
                    }

                    if (fieldInfo.MarshalAsAttribute is object)
                    {
                        field = field.AddAttributeLists(AttributeList(fieldInfo.MarshalAsAttribute));
                    }

                    if (this.HasObsoleteAttribute(fieldDef.GetCustomAttributes()))
                    {
                        field = field.AddAttributeLists(AttributeList(ObsoleteAttributeSyntax));
                        property = property?.AddAttributeLists(AttributeList(ObsoleteAttributeSyntax));
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
                    field = field.AddAttributeLists(AttributeList(FieldOffset(offset)));
                }

                members.Add(field);

                foreach (CustomAttribute bitfieldAttribute in MetadataUtilities.FindAttributes(this.Reader, fieldDef.GetCustomAttributes(), InteropDecorationNamespace, NativeBitfieldAttribute))
                {
                    var fieldTypeInfo = (PrimitiveTypeHandleInfo)fieldDef.DecodeSignature(this.SignatureHandleProvider, null);

                    CustomAttributeValue<TypeSyntax> decodedAttribute = bitfieldAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
                    (int? fieldBitLength, bool signed) = fieldTypeInfo.PrimitiveTypeCode switch
                    {
                        PrimitiveTypeCode.Byte => (8, false),
                        PrimitiveTypeCode.SByte => (8, true),
                        PrimitiveTypeCode.UInt16 => (16, false),
                        PrimitiveTypeCode.Int16 => (16, true),
                        PrimitiveTypeCode.UInt32 => (32, false),
                        PrimitiveTypeCode.Int32 => (32, true),
                        PrimitiveTypeCode.UInt64 => (64, false),
                        PrimitiveTypeCode.Int64 => (64, true),
                        PrimitiveTypeCode.UIntPtr => (null, false),
                        PrimitiveTypeCode.IntPtr => ((int?)null, true),
                        _ => throw new NotImplementedException(),
                    };
                    string propName = (string)decodedAttribute.FixedArguments[0].Value!;
                    byte propOffset = (byte)(long)decodedAttribute.FixedArguments[1].Value!;
                    byte propLength = (byte)(long)decodedAttribute.FixedArguments[2].Value!;
                    if (propLength == 0)
                    {
                        // D3DKMDT_DISPLAYMODE_FLAGS has an "Anonymous" 0-length bitfield,
                        // but that's totally useless and breaks our math later on, so skip it.
                        continue;
                    }

                    long minValue = signed ? -(1L << (propLength - 1)) : 0;
                    long maxValue = (1L << (propLength - (signed ? 1 : 0))) - 1;
                    int? leftPad = fieldBitLength.HasValue ? fieldBitLength - (propOffset + propLength) : null;
                    int rightPad = propOffset;
                    (TypeSyntax propertyType, int propertyBitLength) = propLength switch
                    {
                        1 => (PredefinedType(Token(SyntaxKind.BoolKeyword)), 1),
                        <= 8 => (PredefinedType(Token(signed ? SyntaxKind.SByteKeyword : SyntaxKind.ByteKeyword)), 8),
                        <= 16 => (PredefinedType(Token(signed ? SyntaxKind.ShortKeyword : SyntaxKind.UShortKeyword)), 16),
                        <= 32 => (PredefinedType(Token(signed ? SyntaxKind.IntKeyword : SyntaxKind.UIntKeyword)), 32),
                        <= 64 => (PredefinedType(Token(signed ? SyntaxKind.LongKeyword : SyntaxKind.ULongKeyword)), 64),
                        _ => throw new NotSupportedException(),
                    };

                    AccessorDeclarationSyntax getter = AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .AddModifiers(TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                        .AddAttributeLists(AttributeList(MethodImpl(MethodImplOptions.AggressiveInlining)));
                    AccessorDeclarationSyntax setter = AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .AddAttributeLists(AttributeList(MethodImpl(MethodImplOptions.AggressiveInlining)));

                    ulong maskNoOffset = (1UL << propLength) - 1;
                    ulong mask = maskNoOffset << propOffset;
                    int fieldLengthInHexChars = GetLengthInBytes(fieldTypeInfo.PrimitiveTypeCode) * 2;
                    LiteralExpressionSyntax maskExpr = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(mask, fieldLengthInHexChars), mask));

                    ExpressionSyntax fieldAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(fieldName));
                    TypeSyntax fieldType = field.Declaration.Type.WithoutTrailingTrivia();

                    //// unchecked((int)~mask)
                    ExpressionSyntax notMask = UncheckedExpression(CastExpression(fieldType, PrefixUnaryExpression(SyntaxKind.BitwiseNotExpression, maskExpr)));
                    //// (field & unchecked((int)~mask))
                    ExpressionSyntax fieldAndNotMask = ParenthesizedExpression(BinaryExpression(SyntaxKind.BitwiseAndExpression, fieldAccess, notMask));

                    if (propLength > 1)
                    {
                        LiteralExpressionSyntax maskNoOffsetExpr = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(maskNoOffset, fieldLengthInHexChars), maskNoOffset));
                        ExpressionSyntax notMaskNoOffset = UncheckedExpression(CastExpression(propertyType, PrefixUnaryExpression(SyntaxKind.BitwiseNotExpression, maskNoOffsetExpr)));
                        LiteralExpressionSyntax propOffsetExpr = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(propOffset));

                        // signed:
                        // get => (byte)((field << leftPad) >> (leftPad + rightPad)));
                        // unsigned:
                        // get => (byte)((field >> rightPad) & maskNoOffset);
                        ExpressionSyntax getterExpression =
                            CastExpression(propertyType, ParenthesizedExpression(
                                signed ?
                                    BinaryExpression(
                                        SyntaxKind.RightShiftExpression,
                                        ParenthesizedExpression(BinaryExpression(
                                            SyntaxKind.LeftShiftExpression,
                                            fieldAccess,
                                            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(leftPad!.Value)))),
                                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(leftPad.Value + rightPad)))
                                    : BinaryExpression(
                                        SyntaxKind.BitwiseAndExpression,
                                        ParenthesizedExpression(BinaryExpression(SyntaxKind.RightShiftExpression, fieldAccess, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(rightPad)))),
                                        maskNoOffsetExpr)));
                        getter = getter
                            .WithExpressionBody(ArrowExpressionClause(getterExpression))
                            .WithSemicolonToken(SemicolonWithLineFeed);

                        IdentifierNameSyntax valueName = IdentifierName("value");

                        List<StatementSyntax> setterStatements = new();
                        if (propertyBitLength > propLength)
                        {
                            // The allowed range is smaller than the property type, so we need to check that the value fits.
                            // signed:
                            //  global::System.Debug.Assert(value is >= minValue and <= maxValue);
                            // unsigned:
                            //  global::System.Debug.Assert(value is <= maxValue);
                            RelationalPatternSyntax max = RelationalPattern(TokenWithSpace(SyntaxKind.LessThanEqualsToken), CastExpression(propertyType, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(maxValue))));
                            RelationalPatternSyntax? min = signed ? RelationalPattern(TokenWithSpace(SyntaxKind.GreaterThanEqualsToken), CastExpression(propertyType, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(minValue)))) : null;
                            setterStatements.Add(ExpressionStatement(InvocationExpression(
                                ParseName("global::System.Diagnostics.Debug.Assert"),
                                [Argument(IsPatternExpression(valueName, min is null ? max : BinaryPattern(SyntaxKind.AndPattern, min, max)))])));
                        }

                        // field = (int)((field & unchecked((int)~mask)) | ((int)(value & mask) << propOffset)));
                        ExpressionSyntax valueAndMaskNoOffset = ParenthesizedExpression(BinaryExpression(SyntaxKind.BitwiseAndExpression, valueName, maskNoOffsetExpr));
                        setterStatements.Add(ExpressionStatement(AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            fieldAccess,
                            CastExpression(fieldType, ParenthesizedExpression(
                                BinaryExpression(
                                    SyntaxKind.BitwiseOrExpression,
                                    //// (field & unchecked((int)~mask))
                                    fieldAndNotMask,
                                    //// ((int)(value & mask) << propOffset)
                                    ParenthesizedExpression(BinaryExpression(SyntaxKind.LeftShiftExpression, CastExpression(fieldType, valueAndMaskNoOffset), propOffsetExpr))))))));
                        setter = setter.WithBody(Block([.. setterStatements]));
                    }
                    else
                    {
                        // get => (field & getterMask) != 0;
                        getter = getter
                            .WithExpressionBody(ArrowExpressionClause(BinaryExpression(
                                SyntaxKind.NotEqualsExpression,
                                ParenthesizedExpression(BinaryExpression(SyntaxKind.BitwiseAndExpression, fieldAccess, maskExpr)),
                                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)))))
                            .WithSemicolonToken(SemicolonWithLineFeed);

                        // set => field = (byte)(value ? field | getterMask : field & unchecked((int)~getterMask));
                        setter = setter
                            .WithExpressionBody(ArrowExpressionClause(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    fieldAccess,
                                    CastExpression(
                                        fieldType,
                                        ParenthesizedExpression(
                                            ConditionalExpression(
                                                IdentifierName("value"),
                                                BinaryExpression(SyntaxKind.BitwiseOrExpression, fieldAccess, maskExpr),
                                                fieldAndNotMask))))))
                            .WithSemicolonToken(SemicolonWithLineFeed);
                    }

                    string bitDescription = propLength == 1 ? $"bit {propOffset}" : $"bits {propOffset}-{propOffset + propLength - 1}";
                    string allowedRange = propLength == 1 ? string.Empty : $" Allowed values are [{minValue}..{maxValue}].";

                    PropertyDeclarationSyntax bitfieldProperty = PropertyDeclaration(propertyType.WithTrailingTrivia(Space), Identifier(propName).WithTrailingTrivia(LineFeed))
                        .AddModifiers(TokenWithSpace(this.Visibility))
                        .WithAccessorList(AccessorList(getter, setter))
                        .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>Gets or sets {bitDescription} in the <see cref=\"{fieldName}\" /> field.{allowedRange}</summary>\n"));

                    members.Add(bitfieldProperty);
                }
            }
            catch (Exception ex)
            {
                throw new GenerationFailedException("Failed while generating field: " + fieldName, ex);
            }
        }

        // Add a SizeOf method, if there is a FlexibleArray field.
        if (sizeOfMethod is not null)
        {
            members.Add(sizeOfMethod);
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

        // Surface fields nested within anonymous structs and unions as ref-returning properties on this struct.
        this.AddFlattenedAnonymousMembers(typeDef, context, members);

        StructDeclarationSyntax result = StructDeclaration(name.Identifier, [.. members])
            .WithModifiers([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword)]);

        TypeLayout layout = typeDef.GetLayout();
        CharSet charSet = hasUtf16CharField ? CharSet.Unicode : CharSet.Ansi;
        if (!layout.IsDefault || explicitLayout || charSet != CharSet.Ansi)
        {
            result = result.AddAttributeLists(AttributeList(StructLayout(typeDef.Attributes, layout, charSet)));
        }

        if (this.FindGuidFromAttribute(typeDef) is Guid guid)
        {
            result = result.AddAttributeLists(AttributeList(GUID(guid)));
        }

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);

        // When flattening anonymous nested types, give the leaf fields documentation inherited from the
        // root declaring type's API docs so that the generated ref accessors (which <inheritdoc/> from them)
        // surface meaningful IntelliSense.
        if (this.options.FlattenNestedAnonymousTypes && this.canUseUnscopedRef && typeDef.IsNested && this.ApiDocs is not null)
        {
            TypeDefinition rootDef = typeDef;
            while (rootDef.GetDeclaringType() is { IsNil: false } declaringHandle)
            {
                rootDef = this.Reader.GetTypeDefinition(declaringHandle);
            }

            if (this.ApiDocs.TryGetApiDocs(this.Reader.GetString(rootDef.Name), out ApiDetails? rootDocs))
            {
                result = this.ApplyFieldDocs(result, rootDocs);
            }
        }

        return result;
    }

    private StructDeclarationSyntax DeclareVariableLengthInlineArrayHelper(Context context, TypeSyntax fieldType)
    {
        IdentifierNameSyntax firstElementFieldName = IdentifierName("e0");
        List<MemberDeclarationSyntax> members = new();

        // internal unsafe T e0;
        members.Add(FieldDeclaration(
            [TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword)],
            VariableDeclaration(fieldType, [VariableDeclarator(firstElementFieldName.Identifier)])));

        if (this.canUseUnsafeAdd)
        {
            ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
            ////get { fixed (int** p = &e0) return *(p + index); }
            IdentifierNameSyntax pLocal = IdentifierName("p");
            AccessorDeclarationSyntax getter = AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithBody(Block(
                    FixedStatement(
                        VariableDeclaration(PointerType(fieldType), [VariableDeclarator(pLocal.Identifier, EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, firstElementFieldName)))]),
                        ReturnStatement(PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, ParenthesizedExpression(BinaryExpression(SyntaxKind.AddExpression, pLocal, IdentifierName("index"))))))))
                .AddAttributeLists(AttributeList(MethodImpl(MethodImplOptions.AggressiveInlining)));

            ////[MethodImpl(MethodImplOptions.AggressiveInlining)]
            ////set { fixed (int** p = &e0) *(p + index) = value; }
            AccessorDeclarationSyntax setter = AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                .WithBody(Block(
                    FixedStatement(
                        VariableDeclaration(PointerType(fieldType), [VariableDeclarator(pLocal.Identifier, EqualsValueClause(PrefixUnaryExpression(SyntaxKind.AddressOfExpression, firstElementFieldName)))]),
                        ExpressionStatement(AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, ParenthesizedExpression(BinaryExpression(SyntaxKind.AddExpression, pLocal, IdentifierName("index")))),
                            IdentifierName("value"))))))
                .AddAttributeLists(AttributeList(MethodImpl(MethodImplOptions.AggressiveInlining)));

            ////internal unsafe T this[int index]
            members.Add(IndexerDeclaration(fieldType.WithTrailingTrivia(Space), [Parameter(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), Identifier("index"))])
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .AddAccessorListAccessors(getter, setter));
        }

        // internal partial struct VariableLengthInlineArrayHelper
        return StructDeclaration(Identifier("VariableLengthInlineArrayHelper"), [.. members])
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword));
    }

    private MethodDeclarationSyntax DeclareSizeOfMethod(TypeSyntax structType, TypeSyntax elementType, TypeSyntaxSettings typeSettings)
    {
        PredefinedTypeSyntax intType = PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword));
        IdentifierNameSyntax countName = IdentifierName("count");
        IdentifierNameSyntax localName = IdentifierName("v");
        List<StatementSyntax> statements = new();

        // int v = sizeof(OUTER_STRUCT);
        statements.Add(LocalDeclarationStatement(VariableDeclaration(intType, [VariableDeclarator(localName.Identifier, EqualsValueClause(SizeOfExpression(structType)))])));

        // if (count > 1)
        //   v += checked((count - 1) * sizeof(ELEMENT_TYPE));
        // else if (count < 0)
        //   throw new ArgumentOutOfRangeException(nameof(count));
        statements.Add(IfStatement(
            BinaryExpression(SyntaxKind.GreaterThanExpression, countName, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1))),
            ExpressionStatement(AssignmentExpression(
                SyntaxKind.AddAssignmentExpression,
                localName,
                CheckedExpression(BinaryExpression(
                    SyntaxKind.MultiplyExpression,
                    ParenthesizedExpression(BinaryExpression(SyntaxKind.SubtractExpression, countName, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)))),
                    SizeOfExpression(elementType))))),
            ElseClause(IfStatement(
                BinaryExpression(SyntaxKind.LessThanExpression, countName, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentOutOfRangeException))))).WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken)))).WithCloseParenToken(TokenWithLineFeed(SyntaxKind.CloseParenToken)));

        // return v;
        statements.Add(ReturnStatement(localName));

        // internal static unsafe int SizeOf(int count)
        MethodDeclarationSyntax sizeOfMethod = MethodDeclaration(intType, Identifier("SizeOf"))
            .AddParameterListParameters(Parameter(intType, countName.Identifier))
            .WithBody(Block([.. statements]))
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
            .WithLeadingTrivia(ParseLeadingTrivia("/// <summary>Computes the amount of memory that must be allocated to store this struct, including the specified number of elements in the variable length inline array at the end.</summary>\n"));

        return sizeOfMethod;
    }

    private (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) ReinterpretFieldType(FieldDefinition fieldDef, TypeSyntax originalType, CustomAttributeHandleCollection customAttributes, Context context)
    {
        TypeSyntaxSettings typeSettings = context.Filter(this.fieldTypeSettings);
        TypeHandleInfo fieldTypeHandleInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null);
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
            if (this.useSourceGenerators)
            {
                // TODO: support for marshaling structs with arrays. For now emit Type_unmanaged**.
                return (PointerType(ptr3.ElementType.ToTypeSyntax(typeSettings with { AllowMarshaling = false }, GeneratingElement.Field, null).Type), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
            }
            else
            {
                return (ArrayType(ptr3.ElementType.ToTypeSyntax(typeSettings, GeneratingElement.Field, null).Type, [ArrayRankSpecifier()]), default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
            }
        }

        if (context.AllowMarshaling && this.useSourceGenerators && this.IsManagedType(fieldTypeHandleInfo))
        {
            // TODO: support for marshaling structs with pointers to managed types. For now emit Type_unmanaged*.
            return (fieldTypeHandleInfo.ToTypeSyntax(typeSettings with { AllowMarshaling = false }, GeneratingElement.Field, null).Type, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        return (originalType, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
    }

    private void RequestVariableLengthInlineArrayHelper1(Context context)
    {
        if (this.IsWin32Sdk)
        {
            if (!this.IsTypeAlreadyFullyDeclared($"{this.Namespace}.{this.variableLengthInlineArrayStruct1.Identifier.ValueText}`1"))
            {
                this.DeclareUnscopedRefAttributeIfNecessary();
                this.volatileCode.GenerateSpecialType("VariableLengthInlineArray1", () => this.volatileCode.AddSpecialType("VariableLengthInlineArray1", this.variableLengthInlineArrayStruct1));
            }
        }
        else if (this.SuperGenerator is not null && this.SuperGenerator.TryGetGenerator("Windows.Win32", out Generator? generator))
        {
            generator.volatileCode.GenerationTransaction(delegate
            {
                generator.RequestVariableLengthInlineArrayHelper1(context);
            });
        }
    }

    private void RequestVariableLengthInlineArrayHelper2(Context context)
    {
        if (this.IsWin32Sdk)
        {
            if (!this.IsTypeAlreadyFullyDeclared($"{this.Namespace}.{this.variableLengthInlineArrayStruct2.Identifier.ValueText}`2"))
            {
                this.DeclareUnscopedRefAttributeIfNecessary();
                this.volatileCode.GenerateSpecialType("VariableLengthInlineArray2", () => this.volatileCode.AddSpecialType("VariableLengthInlineArray2", this.variableLengthInlineArrayStruct2));
            }
        }
        else if (this.SuperGenerator is not null && this.SuperGenerator.TryGetGenerator("Windows.Win32", out Generator? generator))
        {
            generator.volatileCode.GenerationTransaction(delegate
            {
                generator.RequestVariableLengthInlineArrayHelper2(context);
            });
        }
    }

    /// <summary>
    /// Surfaces fields nested within anonymous structs and unions as <c>[UnscopedRef] ref</c> properties on the declaring struct
    /// so they can be read, written, and pointed to directly (e.g. <c>value.field</c> instead of <c>value.Anonymous.Anonymous.field</c>).
    /// </summary>
    /// <param name="typeDef">The definition of the struct being generated.</param>
    /// <param name="context">The context the struct is being generated with.</param>
    /// <param name="members">The member list being built for the struct. Generated accessors are appended to the end so that the indices of existing members are preserved.</param>
    private void AddFlattenedAnonymousMembers(TypeDefinition typeDef, Context context, List<MemberDeclarationSyntax> members)
    {
        // The accessors return a ref into a field, which requires [UnscopedRef] (C# 11+).
        if (!this.options.FlattenNestedAnonymousTypes || !this.canUseUnscopedRef)
        {
            return;
        }

        // Collect the names already declared directly on this struct so we never collide with them.
        HashSet<string> reservedNames = new(StringComparer.Ordinal);
        foreach (MemberDeclarationSyntax member in members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        reservedNames.Add(variable.Identifier.ValueText);
                    }

                    break;
                case PropertyDeclarationSyntax property:
                    reservedNames.Add(property.Identifier.ValueText);
                    break;
                case MethodDeclarationSyntax method:
                    reservedNames.Add(method.Identifier.ValueText);
                    break;
            }
        }

        List<MemberDeclarationSyntax> accessors = new();
        HashSet<string> surfacedNames = new(StringComparer.Ordinal);

        foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            if (fieldDef.Attributes.HasFlag(FieldAttributes.Static))
            {
                continue;
            }

            string fieldName = this.Reader.GetString(fieldDef.Name);
            if (!IsAnonymousFieldName(fieldName) || !this.TryGetNestedTypeForAnonymousField(typeDef, fieldDef, out TypeDefinitionHandle nestedTypeHandle))
            {
                continue;
            }

            TypeDefinition nestedTypeDef = this.Reader.GetTypeDefinition(nestedTypeHandle);
            ExpressionSyntax accessPrefix = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(SafeIdentifier(fieldName)));
            NameSyntax crefPrefix = IdentifierName(this.GetMangledIdentifier(this.Reader.GetString(nestedTypeDef.Name), context.AllowMarshaling, this.IsManagedType(nestedTypeHandle)));
            this.GatherFlattenedAnonymousAccessors(nestedTypeHandle, accessPrefix, crefPrefix, context, accessors, surfacedNames, reservedNames, ancestorObsolete: this.HasObsoleteAttribute(fieldDef.GetCustomAttributes()), depth: 0);
        }

        members.AddRange(accessors);
    }

    /// <summary>
    /// Recursively gathers <c>[UnscopedRef] ref</c> accessors for the leaf fields reachable through anonymous holders.
    /// </summary>
    /// <param name="containingTypeHandle">The anonymous nested type currently being walked.</param>
    /// <param name="accessPrefix">The expression that accesses an instance of <paramref name="containingTypeHandle"/> from the outer struct (e.g. <c>this.Anonymous.Anonymous</c>).</param>
    /// <param name="crefTypePrefix">The qualified type-name chain (relative to the outer struct) used to build the <c>cref</c> for inherited documentation.</param>
    /// <param name="containingContext">The context that <paramref name="containingTypeHandle"/> is generated with.</param>
    /// <param name="accessors">The list that generated accessors are appended to.</param>
    /// <param name="surfacedNames">The set of leaf names already surfaced, used to skip duplicates.</param>
    /// <param name="reservedNames">The set of names already declared on the outer struct, used to skip collisions.</param>
    /// <param name="ancestorObsolete"><see langword="true"/> if any anonymous holder field along the path to this type was marked <c>[Obsolete]</c>; such accessors must themselves be marked obsolete to legally reference the obsolete member in their body.</param>
    /// <param name="depth">The current recursion depth, used as a guard against unexpectedly deep nesting.</param>
    private void GatherFlattenedAnonymousAccessors(
        TypeDefinitionHandle containingTypeHandle,
        ExpressionSyntax accessPrefix,
        NameSyntax crefTypePrefix,
        Context containingContext,
        List<MemberDeclarationSyntax> accessors,
        HashSet<string> surfacedNames,
        HashSet<string> reservedNames,
        bool ancestorObsolete,
        int depth)
    {
        // Guard against unexpectedly deep or cyclic nesting.
        if (depth > 8)
        {
            return;
        }

        TypeDefinition containingType = this.Reader.GetTypeDefinition(containingTypeHandle);

        // An explicit-layout type (such as a union) forces its fields to be generated without marshaling,
        // so decode this type's fields with the same context its own members were generated with.
        bool explicitLayout = (containingType.Attributes & TypeAttributes.ExplicitLayout) == TypeAttributes.ExplicitLayout;
        Context bodyContext = containingContext.AllowMarshaling && explicitLayout ? containingContext with { AllowMarshaling = false } : containingContext;
        TypeSyntaxSettings typeSettings = bodyContext.Filter(this.fieldTypeSettings);

        foreach (FieldDefinitionHandle fieldDefHandle in containingType.GetFields())
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            if (fieldDef.Attributes.HasFlag(FieldAttributes.Static))
            {
                continue;
            }

            string fieldName = this.Reader.GetString(fieldDef.Name);
            CustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes();

            // Recurse through anonymous holder fields; named nested members are not flattened.
            if (IsAnonymousFieldName(fieldName) && this.TryGetNestedTypeForAnonymousField(containingType, fieldDef, out TypeDefinitionHandle deeperTypeHandle))
            {
                TypeDefinition deeperTypeDef = this.Reader.GetTypeDefinition(deeperTypeHandle);
                ExpressionSyntax deeperAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, accessPrefix, IdentifierName(SafeIdentifier(fieldName)));
                NameSyntax deeperCref = QualifiedName(crefTypePrefix, IdentifierName(this.GetMangledIdentifier(this.Reader.GetString(deeperTypeDef.Name), bodyContext.AllowMarshaling, this.IsManagedType(deeperTypeHandle))));
                this.GatherFlattenedAnonymousAccessors(deeperTypeHandle, deeperAccess, deeperCref, bodyContext, accessors, surfacedNames, reservedNames, ancestorObsolete || this.HasObsoleteAttribute(fieldAttributes), depth + 1);
                continue;
            }

            // Skip fields whose generated representation is not a plain, ref-returnable field of the decoded type.
            if (this.FindAssociatedEnum(fieldAttributes) is not null)
            {
                // Surfaced as a by-value property rather than a field.
                continue;
            }

            if (this.FindAttribute(fieldAttributes, SystemRuntimeCompilerServices, nameof(FixedBufferAttribute)).HasValue)
            {
                continue;
            }

            if (MetadataUtilities.FindAttribute(this.Reader, fieldAttributes, InteropDecorationNamespace, FlexibleArrayAttribute) is not null)
            {
                continue;
            }

            TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null);

            // Skip field types that are reinterpreted during struct generation (delegates become function pointers,
            // COM interface pointers become arrays, marshaled managed types become unmanaged pointers); the generated
            // member would not be a simple ref-returnable field of the decoded type.
            if (this.IsDelegateReference(fieldTypeInfo, out _))
            {
                continue;
            }

            if (bodyContext.AllowMarshaling && fieldTypeInfo is PointerTypeHandleInfo pointerInfo && this.IsInterface(pointerInfo.ElementType))
            {
                continue;
            }

            if (bodyContext.AllowMarshaling && this.useSourceGenerators && this.IsManagedType(fieldTypeInfo))
            {
                continue;
            }

            TypeSyntax fieldType;
            if (this.TryGetNestedTypeForAnonymousField(containingType, fieldDef, out TypeDefinitionHandle leafNestedTypeHandle))
            {
                // The leaf is a struct/union *value* nested directly within this container (e.g. an anonymous
                // _X_e__Struct). Reference it relative to the outer struct through the same per-twin container
                // chain already used for the cref. The fully qualified form produced by ToTypeSyntax applies the
                // "_unmanaged" suffix uniformly to every ancestor, which names the unmanaged twin
                // (e.g. MSP_EVENT_INFO_unmanaged._Anonymous_e__Union_unmanaged...) rather than the type actually
                // reachable through this.Anonymous on the declaring struct, producing a ref-return mismatch (CS8151).
                TypeDefinition leafNestedType = this.Reader.GetTypeDefinition(leafNestedTypeHandle);
                string leafTypeName = this.GetMangledIdentifier(this.Reader.GetString(leafNestedType.Name), bodyContext.AllowMarshaling, this.IsManagedType(leafNestedTypeHandle));
                fieldType = QualifiedName(crefTypePrefix, IdentifierName(leafTypeName));
            }
            else
            {
                fieldType = fieldTypeInfo.ToTypeSyntax(typeSettings, GeneratingElement.StructMember, fieldAttributes.QualifyWith(this)).Type;
            }

            // Fixed-length arrays are reinterpreted into a helper struct field, which is not a simple ref target.
            if (fieldType is ArrayTypeSyntax)
            {
                continue;
            }

            // Skip collisions with an existing member or another flattened accessor.
            if (reservedNames.Contains(fieldName) || !surfacedNames.Add(fieldName))
            {
                continue;
            }

            // Mark the accessor obsolete when the leaf field (or any holder along its access path) is obsolete,
            // so its body may legally reference the obsolete member without triggering CS0612.
            bool isObsolete = ancestorObsolete || this.HasObsoleteAttribute(fieldAttributes);
            accessors.Add(this.CreateFlattenedAnonymousAccessor(fieldName, fieldType, accessPrefix, crefTypePrefix, isObsolete));
            this.DeclareUnscopedRefAttributeIfNecessary();
        }
    }

    /// <summary>
    /// Creates a single <c>[UnscopedRef] ref</c> property that forwards to a field reached through anonymous holders,
    /// with documentation inherited from the underlying field.
    /// </summary>
    private PropertyDeclarationSyntax CreateFlattenedAnonymousAccessor(string fieldName, TypeSyntax fieldType, ExpressionSyntax accessPrefix, NameSyntax crefTypePrefix, bool isObsolete)
    {
        // ref <FieldType> <FieldName> => ref <accessPrefix>.<FieldName>;
        ExpressionSyntax leafAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, accessPrefix, IdentifierName(SafeIdentifier(fieldName)));
        PropertyDeclarationSyntax accessor = PropertyDeclaration(RefType(fieldType.WithoutTrailingTrivia()).WithTrailingTrivia(Space), SafeIdentifier(fieldName))
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithExpressionBody(ArrowExpressionClause(RefExpression(leafAccess)))
            .WithSemicolonToken(SemicolonWithLineFeed)
            .AddAttributeLists(AttributeList(UnscopedRefAttributeSyntax));

        if (RequiresUnsafe(fieldType))
        {
            accessor = accessor.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
        }

        if (isObsolete)
        {
            accessor = accessor.AddAttributeLists(AttributeList(ObsoleteAttributeSyntax));
        }

        // /// <inheritdoc cref="NestedType.FieldName"/>
        CrefSyntax cref = SyntaxFactory.NameMemberCref(QualifiedName(crefTypePrefix, SafeIdentifierName(fieldName)));
        SyntaxTrivia inheritDoc = Trivia(DocumentationCommentTrivia(
            SyntaxKind.SingleLineDocumentationCommentTrivia,
            [
                XmlText("/// "),
                XmlEmptyElement("inheritdoc", [XmlCrefAttribute(cref)]),
                XmlText(XmlTextNewLine("\n", continueXmlDocumentationComment: false)),
            ]));

        return accessor.WithLeadingTrivia(inheritDoc);
    }

    /// <summary>
    /// Resolves the nested type definition referenced by an anonymous holder field, if the field's type is in fact
    /// a type nested within <paramref name="containingType"/>.
    /// </summary>
    private bool TryGetNestedTypeForAnonymousField(TypeDefinition containingType, FieldDefinition fieldDef, out TypeDefinitionHandle nestedTypeHandle)
    {
        nestedTypeHandle = default;
        if (fieldDef.DecodeSignature(this.SignatureHandleProvider, null) is not HandleTypeHandleInfo fieldTypeInfo || !this.IsNestedType(fieldTypeInfo.Handle))
        {
            return false;
        }

        TypeDefinitionHandle candidate;
        switch (fieldTypeInfo.Handle.Kind)
        {
            case HandleKind.TypeDefinition:
                candidate = (TypeDefinitionHandle)fieldTypeInfo.Handle;
                break;
            case HandleKind.TypeReference when this.TryGetTypeDefHandle((TypeReferenceHandle)fieldTypeInfo.Handle, out QualifiedTypeDefinitionHandle qualifiedHandle) && qualifiedHandle.Generator == this:
                candidate = qualifiedHandle.DefinitionHandle;
                break;
            default:
                return false;
        }

        foreach (TypeDefinitionHandle nested in containingType.GetNestedTypes())
        {
            if (nested == candidate)
            {
                nestedTypeHandle = nested;
                return true;
            }
        }

        return false;
    }
}
