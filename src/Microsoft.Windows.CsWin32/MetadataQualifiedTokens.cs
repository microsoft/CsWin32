// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

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

internal record struct QualifiedParameter(Generator Generator, Parameter Parameter)
{
    internal MetadataReader Reader => this.Generator.Reader;
}

internal record struct QualifiedCustomAttributeHandleCollection(Generator Generator, CustomAttributeHandleCollection Collection) : IEnumerable<QualifiedCustomAttribute>
{
    internal MetadataReader Reader => this.Generator.Reader;

    public IEnumerator<QualifiedCustomAttribute> GetEnumerator()
    {
        foreach (var handle in this.Collection)
        {
            yield return new QualifiedCustomAttribute(this.Generator, this.Reader.GetCustomAttribute(handle));
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
}

internal record struct QualifiedCustomAttribute(Generator Generator, CustomAttribute Attribute)
{
    internal MetadataReader Reader => this.Generator.Reader;
}

internal static class QualifiedExtensions
{
    internal static QualifiedParameter QualifyWith(this Parameter parameter, Generator generator) => new QualifiedParameter(generator, parameter);

    internal static QualifiedCustomAttributeHandleCollection QualifyWith(this CustomAttributeHandleCollection collection, Generator generator) => new QualifiedCustomAttributeHandleCollection(generator, collection);
}
