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

    internal StringHandle Name => this.Method.Name;

    internal QualifiedCustomAttributeHandleCollection? GetReturnTypeCustomAttributes() => this.Generator.GetReturnTypeCustomAttributes(this.Method)?.QualifyWith(this.Generator);

    internal IEnumerable<QualifiedParameterHandle> GetParameters()
    {
        foreach (ParameterHandle parameterHandle in this.Method.GetParameters())
        {
            yield return new QualifiedParameterHandle(this.Generator, parameterHandle);
        }
    }

    internal QualifiedCustomAttributeHandleCollection GetCustomAttributes() => this.Method.GetCustomAttributes().QualifyWith(this.Generator);
}

internal record struct QualifiedCustomAttributeHandle(Generator Generator, CustomAttributeHandle CustomAttributeHandle)
{
    internal MetadataReader Reader => this.Generator.Reader;

    internal QualifiedCustomAttribute Resolve() => new(this.Generator, this.Generator.Reader.GetCustomAttribute(this.CustomAttributeHandle));
}

internal record struct QualifiedParameterHandle(Generator Generator, ParameterHandle ParameterHandle)
{
    internal MetadataReader Reader => this.Generator.Reader;

    internal QualifiedParameter Resolve() => new(this.Generator, this.Generator.Reader.GetParameter(this.ParameterHandle));
}

internal record struct QualifiedParameter(Generator Generator, Parameter Parameter)
{
    internal MetadataReader Reader => this.Generator.Reader;
}

internal record struct QualifiedCustomAttributeHandleCollection(Generator Generator, CustomAttributeHandleCollection Collection) : IEnumerable<QualifiedCustomAttributeHandle>
{
    internal MetadataReader Reader => this.Generator.Reader;

    public IEnumerator<QualifiedCustomAttributeHandle> GetEnumerator()
    {
        foreach (CustomAttributeHandle handle in this.Collection)
        {
            yield return new QualifiedCustomAttributeHandle(this.Generator, handle);
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
    internal static QualifiedMethodDefinition QualifyWith(this MethodDefinition method, Generator generator) => new(generator, method);

    internal static QualifiedParameter QualifyWith(this Parameter parameter, Generator generator) => new(generator, parameter);

    internal static QualifiedParameterHandle QualifyWith(this ParameterHandle parameterHandle, Generator generator) => new(generator, parameterHandle);

    internal static QualifiedCustomAttributeHandleCollection QualifyWith(this CustomAttributeHandleCollection collection, Generator generator) => new(generator, collection);
}
