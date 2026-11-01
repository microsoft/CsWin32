// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// A coordinator of many <see cref="Generator"/> objects, allowing code to be generated that requires types from across many input winmd's.
/// </summary>
public class SuperGenerator : IGenerator, IDisposable
{
    private readonly Dictionary<(string Namespace, string Name), QualifiedMethodDefinitionHandle> winningMethods = new();
    private readonly Dictionary<(string Namespace, string Name), (Generator Generator, FieldDefinitionHandle Handle)> winningConstants = new();

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
    /// <param name="generators">The <see cref="Generator"/> objects in precedence order: later inputs replace earlier types with the same namespace and name. These should <em>not</em> have been added to a <see cref="SuperGenerator"/> previously.</param>
    /// <returns>The new instance of <see cref="SuperGenerator"/>.</returns>
    public static SuperGenerator Combine(params Generator[] generators) => Combine((IEnumerable<Generator>)generators);

    /// <inheritdoc cref="Combine(Generator[])"/>
    public static SuperGenerator Combine(IEnumerable<Generator> generators)
    {
        Generator[] orderedGenerators = generators.ToArray();
        SuperGenerator super = new(orderedGenerators.ToImmutableDictionary(g => g.InputAssemblyName));
        foreach (Generator generator in super.Generators.Values)
        {
            if (generator.SuperGenerator is object)
            {
                throw new InvalidOperationException($"This generator has already been added to a {nameof(SuperGenerator)}.");
            }

            generator.SuperGenerator = super;
        }

        Dictionary<(string Namespace, string Name), QualifiedTypeDefinitionHandle> winningTypes = new();
        foreach (Generator generator in orderedGenerators)
        {
            foreach (Generator earlier in orderedGenerators.TakeWhile(g => g != generator))
            {
                if (generator.ProjectionNamespace.StartsWith(earlier.ProjectionNamespace + ".", StringComparison.Ordinal))
                {
                    generator.ProjectionNamespace = earlier.ProjectionNamespace;
                }
            }

            foreach (NamespaceMetadata ns in generator.MetadataIndex.MetadataByNamespace.Values)
            {
                foreach (var type in ns.Types)
                {
                    winningTypes[(ns.Name, type.Key)] = new(generator, type.Value);
                }

                foreach (var method in ns.Methods)
                {
                    super.winningMethods[(ns.Name, method.Key)] = new(generator, method.Value);
                }

                foreach (var field in ns.Fields)
                {
                    super.winningConstants[(ns.Name, field.Key)] = (generator, field.Value);
                }
            }
        }

        foreach (Generator generator in orderedGenerators)
        {
            foreach (TypeDefinitionHandle handle in generator.Reader.TypeDefinitions)
            {
                TypeDefinition type = generator.Reader.GetTypeDefinition(handle);
                if (type.GetDeclaringType().IsNil &&
                    winningTypes.TryGetValue((generator.Reader.GetString(type.Namespace), generator.Reader.GetString(type.Name)), out QualifiedTypeDefinitionHandle winner) &&
                    winner.Generator != generator)
                {
                    generator.RegisterTypeReplacement(handle, winner);
                }
            }
        }

        return super;
    }

    /// <inheritdoc cref="Generator.TryGenerateAllExternMethods(string, CancellationToken)"/>
    /// <returns>The number of extern methods that were generated.</returns>
    public int TryGenerateAllExternMethods(string moduleName, CancellationToken cancellationToken)
    {
        int matches = 0;
        foreach (Generator generator in this.Generators.Values)
        {
            if (generator.TryGenerateAllExternMethods(moduleName, cancellationToken))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <inheritdoc/>
    public bool TryGenerate(string apiNameOrModuleWildcard, out IReadOnlyCollection<string> preciseApi, CancellationToken cancellationToken) => this.TryGenerate(apiNameOrModuleWildcard, out preciseApi, out _, cancellationToken);

    /// <inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/summary" />
    /// <inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/returns" />
    /// <inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/remarks" />
    /// <param name="apiNameOrModuleWildcard"><inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/param[@name='apiNameOrModuleWildcard']"/></param>
    /// <param name="preciseApi"><inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/param[@name='preciseApi']"/></param>
    /// <param name="redirectedEnums">Receives names of the enum that declares <paramref name="apiNameOrModuleWildcard"/> as an enum value.</param>
    /// <param name="cancellationToken"><inheritdoc cref="TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)" path="/param[@name='cancellationToken']"/></param>
    public bool TryGenerate(string apiNameOrModuleWildcard, out IReadOnlyCollection<string> preciseApi, out IReadOnlyCollection<string> redirectedEnums, CancellationToken cancellationToken)
    {
        HashSet<string> preciseApiAccumulator = new(StringComparer.Ordinal);
        HashSet<string> redirectedEnumsAccumulator = new(StringComparer.Ordinal);
        bool success = false;
        foreach (Generator generator in this.Generators.Values)
        {
            if (generator.TryGenerate(apiNameOrModuleWildcard, out preciseApi, cancellationToken))
            {
                preciseApiAccumulator.UnionWith(preciseApi);
                success = true;
                continue;
            }

            preciseApiAccumulator.UnionWith(preciseApi);
            if (generator.TryGetEnumName(apiNameOrModuleWildcard, out string? declaringEnum))
            {
                redirectedEnumsAccumulator.Add(declaringEnum);
                if (generator.TryGenerate(declaringEnum, out preciseApi, cancellationToken))
                {
                    success = true;
                }

                preciseApiAccumulator.UnionWith(preciseApi);
            }
        }

        preciseApi = preciseApiAccumulator;
        redirectedEnums = redirectedEnumsAccumulator;
        return success;
    }

    /// <inheritdoc/>
    public bool TryGenerateType(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
    {
        List<string> preciseApiAccumulator = new();
        bool success = false;
        foreach (Generator generator in this.Generators.Values)
        {
            success |= generator.TryGenerateType(possiblyQualifiedName, out preciseApi);
            preciseApiAccumulator.AddRange(preciseApi);
        }

        preciseApi = preciseApiAccumulator;
        return success;
    }

    /// <inheritdoc/>
    public void GenerateAllExternMethods(CancellationToken cancellationToken)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateAllExternMethods(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public void GenerateAllConstants(CancellationToken cancellationToken)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateAllConstants(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public void GenerateAllInteropTypes(CancellationToken cancellationToken)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateAllInteropTypes(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public bool TryGenerateExternMethod(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
    {
        List<string> preciseApiAccumulator = new();
        bool success = false;
        foreach (Generator generator in this.Generators.Values)
        {
            success |= generator.TryGenerateExternMethod(possiblyQualifiedName, out preciseApi);
            preciseApiAccumulator.AddRange(preciseApi);
        }

        preciseApi = preciseApiAccumulator;
        return success;
    }

    /// <inheritdoc/>
    public void GenerateAllMacros(CancellationToken cancellationToken)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateAllMacros(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public void GenerateAll(CancellationToken cancellationToken)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateAll(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            if (generator.TryGetEnumName(enumValueName, out declaringEnum))
            {
                return true;
            }
        }

        declaringEnum = null;
        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSuggestions(string name)
    {
        List<string> suggestions = new();
        foreach (Generator generator in this.Generators.Values)
        {
            suggestions.AddRange(generator.GetSuggestions(name).Take(4));
        }

        return suggestions;
    }

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<string, CompilationUnitSyntax>> GetCompilationUnits(CancellationToken cancellationToken)
    {
        return this.Generators.SelectMany(
            g => g.Value.GetCompilationUnits(cancellationToken).Select(kv => new KeyValuePair<string, CompilationUnitSyntax>($"{g.Value.InputAssemblyName}.{kv.Key}", kv.Value)))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.Dispose();
        }
    }

    /// <inheritdoc/>
    public void AddGeneratorExclusion(string exclusion)
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.AddGeneratorExclusion(exclusion);
        }
    }

    /// <summary>
    /// Tells the generator to generate the "real" IDispatch interface fully. This only really has an
    /// effect in COM source generator mode.
    /// </summary>
    public void GenerateIDispatch()
    {
        foreach (Generator generator in this.Generators.Values)
        {
            generator.GenerateFullIDispatch = true;
        }
    }

    internal bool TryGenerateConstant(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi)
    {
        List<string> preciseApiAccumulator = new();
        bool success = false;
        foreach (Generator generator in this.Generators.Values)
        {
            success |= generator.TryGenerateConstant(possiblyQualifiedName, out preciseApi);
            preciseApiAccumulator.AddRange(preciseApi);
        }

        preciseApi = preciseApiAccumulator;
        return success;
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

    /// <summary>
    /// Gets the winning API method from the combined inputs.
    /// </summary>
    /// <param name="generator">The requesting metadata owner.</param>
    /// <param name="handle">The requested method.</param>
    /// <returns>The winning method and its metadata owner.</returns>
    internal QualifiedMethodDefinitionHandle ResolveExternMethod(Generator generator, MethodDefinitionHandle handle)
    {
        MethodDefinition method = generator.Reader.GetMethodDefinition(handle);
        TypeDefinition type = generator.Reader.GetTypeDefinition(method.GetDeclaringType());
        return this.winningMethods.TryGetValue((generator.Reader.GetString(type.Namespace), generator.Reader.GetString(method.Name)), out QualifiedMethodDefinitionHandle winner)
            ? winner : new(generator, handle);
    }

    /// <summary>
    /// Gets the winning API constant from the combined inputs.
    /// </summary>
    /// <param name="generator">The requesting metadata owner.</param>
    /// <param name="handle">The requested constant.</param>
    /// <returns>The winning constant and its metadata owner.</returns>
    internal (Generator Generator, FieldDefinitionHandle Handle) ResolveConstant(Generator generator, FieldDefinitionHandle handle)
    {
        FieldDefinition field = generator.Reader.GetFieldDefinition(handle);
        TypeDefinition type = generator.Reader.GetTypeDefinition(field.GetDeclaringType());
        return this.winningConstants.TryGetValue((generator.Reader.GetString(type.Namespace), generator.Reader.GetString(field.Name)), out var winner)
            ? winner : (generator, handle);
    }

    /// <summary>
    /// Gets the generator responsible for shared projection helpers in a namespace.
    /// </summary>
    /// <param name="generator">The generator requesting a shared helper.</param>
    /// <returns>A generator using the same projection namespace.</returns>
    internal Generator GetNamespaceOwner(Generator generator) =>
        this.Generators.Values.Where(g => g.ProjectionNamespace == generator.ProjectionNamespace)
            .OrderByDescending(g => g.MetadataIndex.CommonNamespace == g.ProjectionNamespace)
            .ThenBy(g => g.InputAssemblyName, StringComparer.Ordinal).First();

    internal Generator GetGeneratorFromReader(MetadataReader reader)
    {
        return this.Generators.FirstOrDefault(kv => kv.Value.Reader == reader).Value ?? throw new InvalidOperationException("No generator found for the specified reader.");
    }

    internal bool TryGetTypeDefinitionHandle(QualifiedTypeReferenceHandle typeRefHandle, out QualifiedTypeDefinitionHandle typeDefHandle)
    {
        if (typeRefHandle.Generator.TryGetTypeDefHandle(typeRefHandle.ReferenceHandle, out TypeDefinitionHandle localHandle))
        {
            typeDefHandle = typeRefHandle.Generator.ResolveTypeDefinition(localHandle);
            return true;
        }

        QualifiedTypeReference typeRef = typeRefHandle.Resolve();
        if (this.TryGetTargetGenerator(typeRef, out Generator? targetGenerator))
        {
            string? @namespace = typeRef.Generator.Reader.GetString(typeRef.Reference.Namespace);
            string? @name = typeRef.Generator.Reader.GetString(typeRef.Reference.Name);
            if (targetGenerator.TryGetTypeDefHandle(@namespace, name, out TypeDefinitionHandle targetTypeDefHandle))
            {
                typeDefHandle = targetGenerator.ResolveTypeDefinition(targetTypeDefHandle);
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
    /// <param name="context">The generation context.</param>
    /// <returns><see langword="true" /> if a matching generator was found; <see langword="false" /> otherwise.</returns>
    internal bool TryRequestInteropType(QualifiedTypeReference typeRef, Generator.Context context)
    {
        if (typeRef.Reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            throw new ArgumentException("Only type references across assemblies should be requested.", nameof(typeRef));
        }

        if (this.TryGetTargetGenerator(typeRef, out Generator? generator))
        {
            string ns = typeRef.Generator.Reader.GetString(typeRef.Reference.Namespace);
            string name = typeRef.Generator.Reader.GetString(typeRef.Reference.Name);
            generator.RequestInteropType(ns, name, context);
            return true;
        }

        return false;
    }
}
