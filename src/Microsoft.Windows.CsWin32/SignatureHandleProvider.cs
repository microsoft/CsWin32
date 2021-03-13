﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Immutable;
    using System.Reflection.Metadata;

    internal class SignatureHandleProvider : ISignatureTypeProvider<TypeHandleInfo, SignatureHandleProvider.IGenericContext?>
    {
        internal static readonly SignatureHandleProvider Instance = new SignatureHandleProvider();

        private SignatureHandleProvider()
        {
        }

        internal interface IGenericContext
        {
        }

        public TypeHandleInfo GetArrayType(TypeHandleInfo elementType, ArrayShape shape) => new ArrayTypeHandleInfo(elementType, shape);

        public TypeHandleInfo GetPointerType(TypeHandleInfo elementType) => new PointerTypeHandleInfo(elementType);

        public TypeHandleInfo GetPrimitiveType(PrimitiveTypeCode typeCode) => new PrimitiveTypeHandleInfo(typeCode);

        public TypeHandleInfo GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => new HandleTypeHandleInfo(reader, handle, rawTypeKind);

        public TypeHandleInfo GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => new HandleTypeHandleInfo(reader, handle, rawTypeKind);

        /// <inheritdoc/>
        public TypeHandleInfo GetSZArrayType(TypeHandleInfo elementType) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetTypeFromSpecification(MetadataReader reader, IGenericContext? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetByReferenceType(TypeHandleInfo elementType) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetFunctionPointerType(MethodSignature<TypeHandleInfo> signature) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetGenericInstantiation(TypeHandleInfo genericType, ImmutableArray<TypeHandleInfo> typeArguments) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetGenericMethodParameter(IGenericContext? genericContext, int index) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetGenericTypeParameter(IGenericContext? genericContext, int index) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetModifiedType(TypeHandleInfo modifier, TypeHandleInfo unmodifiedType, bool isRequired) => throw new NotImplementedException();

        /// <inheritdoc/>
        public TypeHandleInfo GetPinnedType(TypeHandleInfo elementType) => throw new NotImplementedException();
    }
}
