// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// Extension methods for the <see cref="IGenerator"/> interface.
/// </summary>
public static class GeneratorExtensions
{
    /// <inheritdoc cref="IGenerator.TryGenerate(string, out IReadOnlyCollection{string}, CancellationToken)"/>
    public static bool TryGenerate(this IGenerator generator, string apiNameOrModuleWildcard, CancellationToken cancellationToken) => generator.TryGenerate(apiNameOrModuleWildcard, out _, cancellationToken);

    /// <inheritdoc cref="IGenerator.TryGenerateType(string, out IReadOnlyCollection{string})"/>
    public static bool TryGenerateType(this IGenerator generator, string possiblyQualifiedName) => generator.TryGenerateType(possiblyQualifiedName, out _);
}
