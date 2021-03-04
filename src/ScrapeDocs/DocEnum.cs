// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ScrapeDocs
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection.Metadata;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static GeneratorUtilities;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal class DocEnum
    {
        private static readonly AttributeListSyntax FlagsAttributeList = AttributeList().AddAttributes(Attribute(IdentifierName("Flags")));

        internal DocEnum(bool isFlags, IReadOnlyDictionary<string, (ulong? Value, string? Doc)> members)
        {
            this.IsFlags = isFlags;
            this.Members = members;
        }

        internal bool IsFlags { get; }

        internal IReadOnlyDictionary<string, (ulong? Value, string? Doc)> Members { get; }

        public override bool Equals(object? obj) => this.Equals(obj as DocEnum);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = this.IsFlags ? 1 : 0;
                foreach (KeyValuePair<string, (ulong? Value, string? Doc)> entry in this.Members)
                {
                    hash += entry.Key.GetHashCode();
                    hash += (int)(entry.Value.Value ?? 0u);
                }

                return hash;
            }
        }

        public bool Equals(DocEnum? other)
        {
            if (other is null)
            {
                return false;
            }

            if (this.IsFlags != other.IsFlags)
            {
                return false;
            }

            if (this.Members.Count != other.Members.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, (ulong? Value, string? Doc)> entry in this.Members)
            {
                if (!other.Members.TryGetValue(entry.Key, out (ulong? Value, string? Doc) value))
                {
                    return false;
                }

                if (entry.Value.Value != value.Value)
                {
                    return false;
                }
            }

            return true;
        }

        internal (string Namespace, EnumDeclarationSyntax Enum)? Emit(string name, MetadataReader mr, HashSet<TypeDefinitionHandle> apiClassHandles)
        {
            if (this.Members.Count == 2 && this.Members.ContainsKey("TRUE") && this.Members.ContainsKey("FALSE"))
            {
                return null;
            }

            PredefinedTypeSyntax? baseType = null;
            string? ns = null;

            // Look up values for each constant.
            var values = new Dictionary<string, ExpressionSyntax>();
            foreach (var item in this.Members)
            {
                bool found = false;
                foreach (FieldDefinitionHandle handle in mr.FieldDefinitions)
                {
                    FieldDefinition fieldDef = mr.GetFieldDefinition(handle);
                    if (apiClassHandles.Contains(fieldDef.GetDeclaringType()) && mr.StringComparer.Equals(fieldDef.Name, item.Key))
                    {
                        found = true;
                        Constant constant = mr.GetConstant(fieldDef.GetDefaultValue());
                        values.Add(item.Key, this.IsFlags ? ToHexExpressionSyntax(mr, constant) : ToExpressionSyntax(mr, constant));
                        baseType ??= ToTypeOfConstant(mr, constant);
                        ns ??= mr.GetString(mr.GetTypeDefinition(fieldDef.GetDeclaringType()).Namespace);
                        break;
                    }
                }

                if (!found)
                {
                    // We couldn't find all the constants required.
                    return null;
                }
            }

            if (baseType is null || ns is null)
            {
                // We don't know all the values.
                return null;
            }

            // Strip the method's declaring interface from the enum name, where applicable.
            NameSyntax enumNameSyntax = ParseName(name);
            if (enumNameSyntax is QualifiedNameSyntax qname)
            {
                enumNameSyntax = qname.Right;
            }

            EnumDeclarationSyntax enumDecl = EnumDeclaration(Identifier(enumNameSyntax.ToString()))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddMembers(this.Members
                    .Select(kv => EnumMemberDeclaration(Identifier(kv.Key)).WithEqualsValue(EqualsValueClause(values[kv.Key]))).ToArray());

            if (this.IsFlags)
            {
                // For flags enums, prefer typing as unsigned integers.
                baseType = PredefinedType(Token(baseType.Keyword.Kind() switch
                {
                    SyntaxKind.ShortKeyword => SyntaxKind.UShortKeyword,
                    SyntaxKind.IntKeyword => SyntaxKind.UIntKeyword,
                    SyntaxKind.LongKeyword => SyntaxKind.ULongKeyword,
                    _ => baseType.Keyword.Kind(),
                }));
            }

            if (baseType is not PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.IntKeyword } })
            {
                enumDecl = enumDecl.AddBaseListTypes(SimpleBaseType(baseType));
            }

            if (this.IsFlags)
            {
                enumDecl = enumDecl.AddAttributeLists(FlagsAttributeList);
            }

            return (ns, enumDecl);
        }
    }
}
