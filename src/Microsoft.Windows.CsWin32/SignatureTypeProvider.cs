// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

    internal class SignatureTypeProvider : ISignatureTypeProvider<TypeSyntax, IGenericContext?>
    {
        private readonly Generator owner;

        internal SignatureTypeProvider(Generator owner)
        {
            this.owner = owner;
        }

        /// <inheritdoc/>
        public TypeSyntax GetPointerType(TypeSyntax elementType) => PointerType(elementType);

        /// <inheritdoc/>
        public TypeSyntax GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Char => PredefinedType(Token(SyntaxKind.CharKeyword)),
                PrimitiveTypeCode.Boolean => PredefinedType(Token(SyntaxKind.BoolKeyword)),
                PrimitiveTypeCode.SByte => PredefinedType(Token(SyntaxKind.SByteKeyword)),
                PrimitiveTypeCode.Byte => PredefinedType(Token(SyntaxKind.ByteKeyword)),
                PrimitiveTypeCode.Int16 => PredefinedType(Token(SyntaxKind.ShortKeyword)),
                PrimitiveTypeCode.UInt16 => PredefinedType(Token(SyntaxKind.UShortKeyword)),
                PrimitiveTypeCode.Int32 => PredefinedType(Token(SyntaxKind.IntKeyword)),
                PrimitiveTypeCode.UInt32 => PredefinedType(Token(SyntaxKind.UIntKeyword)),
                PrimitiveTypeCode.Int64 => PredefinedType(Token(SyntaxKind.LongKeyword)),
                PrimitiveTypeCode.UInt64 => PredefinedType(Token(SyntaxKind.ULongKeyword)),
                PrimitiveTypeCode.Single => PredefinedType(Token(SyntaxKind.FloatKeyword)),
                PrimitiveTypeCode.Double => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
                PrimitiveTypeCode.Object => PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                PrimitiveTypeCode.String => PredefinedType(Token(SyntaxKind.StringKeyword)),
                PrimitiveTypeCode.IntPtr => this.owner.LanguageVersion >= LanguageVersion.CSharp9 ? IdentifierName("nint") : IdentifierName(nameof(IntPtr)),
                PrimitiveTypeCode.UIntPtr => this.owner.LanguageVersion >= LanguageVersion.CSharp9 ? IdentifierName("nuint") : IdentifierName(nameof(UIntPtr)),
                PrimitiveTypeCode.Void => PredefinedType(Token(SyntaxKind.VoidKeyword)),
                _ => throw new NotSupportedException("Unsupported type code: " + typeCode),
            };
        }

        /// <inheritdoc/>
        public TypeSyntax GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var td = reader.GetTypeDefinition(handle);
            string name = reader.GetString(td.Name);

            // Take this opportunity to ensure the type exists too.
            if (Generator.BclInteropStructs.TryGetValue(name, out TypeSyntax? bclType))
            {
                return bclType;
            }

            this.owner.GenerateInteropType(handle);
            TypeSyntax identifier = IdentifierName(name);

            if ((td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
            {
                identifier = PointerType(identifier);
            }

            return identifier;
        }

        /// <inheritdoc/>
        public TypeSyntax GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference tr = reader.GetTypeReference(handle);
            string name = reader.GetString(tr.Name);

            // Take this opportunity to ensure the type exists too.
            if (Generator.BclInteropStructs.TryGetValue(name, out TypeSyntax? bclType))
            {
                return bclType;
            }

            TypeDefinitionHandle? typeDefHandle = this.owner.GenerateInteropType(handle);
            if (typeDefHandle.HasValue)
            {
                TypeSyntax identifier = IdentifierName(name);
                TypeDefinition td = reader.GetTypeDefinition(typeDefHandle.Value);
                if ((td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
                {
                    identifier = PointerType(identifier);
                }

                return identifier;
            }
            else
            {
                // Fully qualify the type, since it may come in from another assembly and may come from a different namespace that isn't imported.
                return QualifiedName(ParseName("global::" + reader.GetString(tr.Namespace)), IdentifierName(name))
                    .WithAdditionalAnnotations(new SyntaxAnnotation(Generator.IsManagedTypeAnnotation, "true")); // an assumption for now.
            }
        }

        /// <inheritdoc/>
        public TypeSyntax GetArrayType(TypeSyntax elementType, ArrayShape shape)
        {
            if (shape.LowerBounds[0] > 0)
            {
                throw new NotSupportedException();
            }

            return ArrayType(elementType, SingletonList(ArrayRankSpecifier().AddSizes(shape.Sizes.Select(size => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(size))).ToArray<ExpressionSyntax>())));
        }

        /// <inheritdoc/>
        public TypeSyntax GetByReferenceType(TypeSyntax elementType) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetFunctionPointerType(MethodSignature<TypeSyntax> signature) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetGenericInstantiation(TypeSyntax genericType, ImmutableArray<TypeSyntax> typeArguments) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetGenericMethodParameter(IGenericContext? genericContext, int index) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetGenericTypeParameter(IGenericContext? genericContext, int index) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetModifiedType(TypeSyntax modifier, TypeSyntax unmodifiedType, bool isRequired) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetPinnedType(TypeSyntax elementType) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetSZArrayType(TypeSyntax elementType) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeSyntax GetTypeFromSpecification(MetadataReader reader, IGenericContext? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => throw new NotImplementedException();
    }

#pragma warning disable SA1201 // Elements should appear in the correct order
    internal interface IGenericContext
#pragma warning restore SA1201 // Elements should appear in the correct order
    {
    }
}
