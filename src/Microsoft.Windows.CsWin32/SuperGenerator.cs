// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// A coordinator of many <see cref="Generator"/> objects, allowing code to be generated that requires types from across many input winmd's.
/// </summary>
public class SuperGenerator
{
    private SuperGenerator(ImmutableDictionary<string, Generator> generators)
    {
        this.Generators = generators;
    }

    /// <summary>
    /// Gets the collection of generators managed by this <see cref="SuperGenerator"/>, indexed by their input winmd's.
    /// </summary>
    public ImmutableDictionary<string, Generator> Generators { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperGenerator"/> class.
    /// </summary>
    /// <param name="generators">The <see cref="Generator"/> objects to enable collaborative generation across. These should <em>not</em> have been added to a <see cref="SuperGenerator"/> previously.</param>
    /// <returns>The new instance of <see cref="SuperGenerator"/>.</returns>
    public static SuperGenerator Combine(params Generator[] generators) => Combine((IEnumerable<Generator>)generators);

    /// <inheritdoc cref="Combine(Generator[])"/>
    public static SuperGenerator Combine(IEnumerable<Generator> generators)
    {
        SuperGenerator super = new(generators.ToImmutableDictionary(g => g.InputAssemblyName));
        foreach (Generator generator in super.Generators.Values)
        {
            if (generator.SuperGenerator is object)
            {
                throw new InvalidOperationException($"This generator has already been added to a {nameof(SuperGenerator)}.");
            }

            generator.SuperGenerator = super;
        }

        return super;
    }

    /// <summary>
    /// Looks up the <see cref="Generator"/> that owns a referenced type.
    /// </summary>
    /// <param name="typeRef">The generator and type reference from the requesting generator.</param>
    /// <param name="targetGenerator">Receives the generator that owns the referenced type.</param>
    /// <returns><see langword="true"/> if a matching generator was found; otherwise <see langword="false"/>.</returns>
    internal bool TryGetTargetGenerator(QualifiedTypeReference typeRef, [NotNullWhen(true)] out Generator? targetGenerator)
    {
        if (typeRef.Reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            targetGenerator = null;
            return false;
        }

        AssemblyReference assemblyRef = typeRef.Generator.Reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.Reference.ResolutionScope);
        string scope = typeRef.Generator.Reader.GetString(assemblyRef.Name);
        return this.TryGetGenerator(scope, out targetGenerator);
    }

    internal bool TryGetGenerator(string winmdName, [NotNullWhen(true)] out Generator? targetGenerator)
    {
        // Workaround the fact that these winmd references may oddly have .winmd included as a suffix.
        if (winmdName.EndsWith(".winmd", StringComparison.OrdinalIgnoreCase))
        {
            winmdName = winmdName.Substring(0, winmdName.Length - ".winmd".Length);
        }

        return this.Generators.TryGetValue(winmdName, out targetGenerator);
    }

    internal bool TryGetTypeDefinitionHandle(QualifiedTypeReferenceHandle typeRefHandle, out QualifiedTypeDefinitionHandle typeDefHandle)
    {
        if (typeRefHandle.Generator.TryGetTypeDefHandle(typeRefHandle.ReferenceHandle, out TypeDefinitionHandle localHandle))
        {
            typeDefHandle = new QualifiedTypeDefinitionHandle(typeRefHandle.Generator, localHandle);
            return true;
        }

        QualifiedTypeReference typeRef = typeRefHandle.Resolve();
        if (this.TryGetTargetGenerator(typeRef, out Generator? targetGenerator))
        {
            string? @namespace = typeRef.Generator.Reader.GetString(typeRef.Reference.Namespace);
            string? @name = typeRef.Generator.Reader.GetString(typeRef.Reference.Name);
            if (targetGenerator.TryGetTypeDefHandle(@namespace, name, out TypeDefinitionHandle targetTypeDefHandle))
            {
                typeDefHandle = new QualifiedTypeDefinitionHandle(targetGenerator, targetTypeDefHandle);
                return true;
            }
        }

        typeDefHandle = default;
        return false;
    }

    /// <summary>
    /// Requests generation of a type referenced across metadata files.
    /// </summary>
    /// <param name="typeRef">The referenced type, with generator.</param>
    /// <returns><see langword="true" /> if a matching generator was found; <see langword="false" /> otherwise.</returns>
    internal bool TryRequestInteropType(QualifiedTypeReference typeRef)
    {
        if (typeRef.Reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            throw new ArgumentException("Only type references across assemblies should be requested.", nameof(typeRef));
        }

        if (this.TryGetTargetGenerator(typeRef, out Generator? generator))
        {
            string ns = typeRef.Generator.Reader.GetString(typeRef.Reference.Namespace);
            string name = typeRef.Generator.Reader.GetString(typeRef.Reference.Name);
            generator.RequestInteropType(ns, name);
            return true;
        }

        return false;
    }
}
