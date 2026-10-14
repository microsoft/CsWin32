// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <inheritdoc/>
    public bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum) => this.MetadataIndex.TryGetEnumName(this.Reader, enumValueName, out declaringEnum);

    private EnumDeclarationSyntax DeclareEnum(TypeDefinitionHandle typeHandle, TypeDefinition typeDef)
    {
        bool flagsEnum = this.FindAttribute(typeDef.GetCustomAttributes(), nameof(System), nameof(FlagsAttribute)) is not null;
        PrimitiveTypeCode storage = this.GetProjectedEnumUnderlyingType(typeHandle);
        bool changedStorage = storage != GetDeclaredEnumUnderlyingType(new(this, this.Reader, typeHandle));

        var enumValues = new List<SyntaxNodeOrToken>();
        TypeSyntax enumBaseType = PrimitiveTypeHandleInfo.ToTypeSyntax(storage, this.enumTypeSettings.PreferNativeInt);
        foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
        {
            AddEnumValue(fieldDefHandle);
        }

        // Add associated constants.
        foreach (CustomAttribute associatedConstAtt in MetadataUtilities.FindAttributes(this.Reader, typeDef.GetCustomAttributes(), InteropDecorationNamespace, AssociatedConstantAttribute))
        {
            CustomAttributeValue<TypeSyntax> decodedAttribute = associatedConstAtt.DecodeValue(CustomAttributeTypeProvider.Instance);
            if (decodedAttribute.FixedArguments.Length >= 1 && decodedAttribute.FixedArguments[0].Value is string constName)
            {
                if (TryFindConstant(constName, out FieldDefinitionHandle fieldHandle))
                {
                    AddEnumValue(fieldHandle);
                }
            }
        }

        string? name = this.Reader.GetString(typeDef.Name);
        EnumDeclarationSyntax result = EnumDeclaration(Identifier(name), SeparatedList<EnumMemberDeclarationSyntax>(enumValues))
            .WithModifiers([TokenWithSpace(this.Visibility)]);

        if (!(enumBaseType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } }))
        {
            result = result.WithIdentifier(result.Identifier.WithTrailingTrivia(Space))
                .WithBaseList(BaseList(SimpleBaseType(enumBaseType).WithTrailingTrivia(LineFeed)).WithColonToken(TokenWithSpace(SyntaxKind.ColonToken)));
        }

        if (flagsEnum)
        {
            result = result.AddAttributeLists(
                AttributeList(FlagsAttributeSyntax).WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)));
        }

        result = this.AddApiDocumentation(name, result);

        return result;

        void AddEnumValue(FieldDefinitionHandle fieldDefHandle)
        {
            FieldDefinition fieldDef = this.Reader.GetFieldDefinition(fieldDefHandle);
            string enumValueName = this.Reader.GetString(fieldDef.Name);
            ConstantHandle valueHandle = fieldDef.GetDefaultValue();
            if (valueHandle.IsNil)
            {
                return;
            }

            bool enumBaseTypeIsSigned = enumBaseType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.LongKeyword or (int)SyntaxKind.IntKeyword or (int)SyntaxKind.ShortKeyword or (int)SyntaxKind.SByteKeyword } };
            ExpressionSyntax enumValue = flagsEnum ? ToHexExpressionSyntax(this.Reader, valueHandle, enumBaseTypeIsSigned) : ToExpressionSyntax(this.Reader, valueHandle);
            if (changedStorage)
            {
                Constant constant = this.Reader.GetConstant(valueHandle);
                decimal value = Convert.ToDecimal(this.Reader.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode), CultureInfo.InvariantCulture);
                (decimal minimum, decimal maximum) = storage switch
                {
                    PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte => (sbyte.MinValue, byte.MaxValue),
                    PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => (short.MinValue, ushort.MaxValue),
                    PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 => (int.MinValue, uint.MaxValue),
                    _ => (long.MinValue, (decimal)ulong.MaxValue),
                };
                if (value < minimum || value > maximum)
                {
                    throw new GenerationFailedException($"AssociatedEnum member {enumValueName}={value} does not fit native {storage} storage without truncation.");
                }

                enumValue = UncheckedExpression(CastExpression(enumBaseType, enumValue));
            }

            EnumMemberDeclarationSyntax enumMember = EnumMemberDeclaration(SafeIdentifier(enumValueName), EqualsValueClause(enumValue));
            enumValues.Add(enumMember);
            enumValues.Add(TokenWithLineFeed(SyntaxKind.CommaToken));
        }

        bool TryFindConstant(string name, out FieldDefinitionHandle fieldDefinitionHandle)
        {
            foreach (var ns in this.MetadataIndex.MetadataByNamespace)
            {
                if (ns.Value.Fields.TryGetValue(name, out fieldDefinitionHandle))
                {
                    return true;
                }
            }

            fieldDefinitionHandle = default;
            return false;
        }
    }
}
