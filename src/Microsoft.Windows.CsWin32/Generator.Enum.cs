﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;
using static Microsoft.Windows.CsWin32.SimpleSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <inheritdoc cref="MetadataIndex.TryGetEnumName(MetadataReader, string, out string?)"/>
    public bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum) => this.MetadataIndex.TryGetEnumName(this.Reader, enumValueName, out declaringEnum);

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
            ExpressionSyntax enumValue = flagsEnum ? ToHexExpressionSyntax(this.Reader, valueHandle, enumBaseTypeIsSigned) : ToExpressionSyntax(this.Reader, valueHandle);
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
}
