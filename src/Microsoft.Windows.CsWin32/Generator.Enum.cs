// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <inheritdoc/>
    public bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum) => this.MetadataIndex.TryGetEnumName(this.Reader, enumValueName, out declaringEnum);

    private EnumDeclarationSyntax DeclareEnum(TypeDefinition typeDef)
    {
        bool flagsEnum = this.FindAttribute(typeDef.GetCustomAttributes(), nameof(System), nameof(FlagsAttribute)) is not null;

        var enumValues = new List<SyntaxNodeOrToken>();
        TypeSyntax? enumBaseType = null;
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

        if (enumBaseType is null)
        {
            throw new NotSupportedException("Unknown enum type.");
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
                enumBaseType = fieldDef.DecodeSignature(this.SignatureHandleProvider, null).ToTypeSyntax(this.enumTypeSettings, GeneratingElement.EnumValue, null).Type;
                return;
            }

            bool enumBaseTypeIsSigned = enumBaseType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.LongKeyword or (int)SyntaxKind.IntKeyword or (int)SyntaxKind.ShortKeyword or (int)SyntaxKind.SByteKeyword } };
            ExpressionSyntax enumValue = flagsEnum ? ToHexExpressionSyntax(this.Reader, valueHandle, enumBaseTypeIsSigned) : ToExpressionSyntax(this.Reader, valueHandle);
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
