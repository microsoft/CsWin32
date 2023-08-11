// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// The generator interface implemented by <see cref="Generator"/> and <see cref="SuperGenerator"/>.
/// </summary>
public interface IGenerator : IDisposable
{
    /// <summary>
    /// Generates code for a given API.
    /// </summary>
    /// <param name="apiNameOrModuleWildcard">The name of the method, struct or constant. Or the name of a module with a ".*" suffix in order to generate all methods and supporting types for the specified module.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="apiNameOrModuleWildcard"/> matched on.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true" /> if any matching APIs were found and generated; <see langword="false"/> otherwise.</returns>
    bool TryGenerate(string apiNameOrModuleWildcard, out IReadOnlyCollection<string> preciseApi, CancellationToken cancellationToken);

    /// <summary>
    /// Collects the result of code generation.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All the generated source files, keyed by filename.</returns>
    IEnumerable<KeyValuePair<string, CompilationUnitSyntax>> GetCompilationUnits(CancellationToken cancellationToken);

    /// <summary>
    /// Generate code for the named type, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the interop type, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the type generated; otherwise <see langword="false"/>.</returns>
    bool TryGenerateType(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi);

    /// <summary>
    /// Generates a projection of all extern methods and their supporting types.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    void GenerateAllExternMethods(CancellationToken cancellationToken);

    /// <summary>
    /// Generates a projection of all constants.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    void GenerateAllConstants(CancellationToken cancellationToken);

    /// <summary>
    /// Generates a projection that includes all structs, interfaces, and other interop types.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    void GenerateAllInteropTypes(CancellationToken cancellationToken);

    /// <summary>
    /// Generate code for the named extern method, if it is recognized.
    /// </summary>
    /// <param name="possiblyQualifiedName">The name of the extern method, optionally qualified with a namespace.</param>
    /// <param name="preciseApi">Receives the canonical API names that <paramref name="possiblyQualifiedName"/> matched on.</param>
    /// <returns><see langword="true"/> if a match was found and the extern method generated; otherwise <see langword="false"/>.</returns>
    bool TryGenerateExternMethod(string possiblyQualifiedName, out IReadOnlyCollection<string> preciseApi);

    /// <summary>
    /// Generates a projection of all macros.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    void GenerateAllMacros(CancellationToken cancellationToken);

    /// <summary>
    /// Generates all extern methods, structs, delegates, constants as defined by the source metadata.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    void GenerateAll(CancellationToken cancellationToken);

    /// <inheritdoc cref="MetadataIndex.TryGetEnumName(MetadataReader, string, out string?)"/>
    bool TryGetEnumName(string enumValueName, [NotNullWhen(true)] out string? declaringEnum);

    /// <summary>
    /// Produces a sequence of suggested APIs with a similar name to the specified one.
    /// </summary>
    /// <param name="name">The user-supplied name.</param>
    /// <returns>A sequence of API names.</returns>
    IReadOnlyList<string> GetSuggestions(string name);
}
