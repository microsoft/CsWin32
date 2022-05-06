// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.Metadata;

namespace Microsoft.Windows.CsWin32;

#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
#pragma warning disable SA1649 // Filenames must match first declared type

internal record struct QualifiedTypeReferenceHandle(Generator Generator, TypeReferenceHandle ReferenceHandle)
{
    internal MetadataReader Reader => this.Generator.Reader;

    internal QualifiedTypeReference Resolve() => new(this.Generator, this.Generator.Reader.GetTypeReference(this.ReferenceHandle));
}

internal record struct QualifiedTypeReference(Generator Generator, TypeReference Reference)
{
    internal MetadataReader Reader => this.Generator.Reader;
}

internal record struct QualifiedTypeDefinitionHandle(Generator Generator, TypeDefinitionHandle DefinitionHandle)
{
    internal MetadataReader Reader => this.Generator.Reader;

    internal QualifiedTypeDefinition Resolve() => new(this.Generator, this.Generator.Reader.GetTypeDefinition(this.DefinitionHandle));
}

internal record struct QualifiedTypeDefinition(Generator Generator, TypeDefinition Definition)
{
    internal MetadataReader Reader => this.Generator.Reader;
}

internal record struct QualifiedMethodDefinitionHandle(Generator Generator, MethodDefinitionHandle MethodHandle)
{
    internal MetadataReader Reader => this.Generator.Reader;

    internal QualifiedMethodDefinition Resolve() => new(this.Generator, this.Generator.Reader.GetMethodDefinition(this.MethodHandle));
}

internal record struct QualifiedMethodDefinition(Generator Generator, MethodDefinition Method)
{
    internal MetadataReader Reader => this.Generator.Reader;
}
