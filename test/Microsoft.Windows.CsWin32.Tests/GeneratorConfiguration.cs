// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

internal record GeneratorConfiguration
{
    internal static GeneratorConfiguration Default { get; } = new();

    internal ImmutableArray<string> InputMetadataPaths { get; init; } = CollectAssemblyMetadata("ProjectionMetadataWinmd");

    internal ImmutableArray<string> InputDocPaths { get; init; } = CollectAssemblyMetadata("ProjectionDocs");

    internal string ToGlobalConfigString()
    {
        StringBuilder globalConfigBuilder = new();
        globalConfigBuilder.AppendLine("is_global = true");
        globalConfigBuilder.AppendLine();
        AddPathsProperty("CsWin32InputMetadataPaths", this.InputMetadataPaths);
        AddPathsProperty("CsWin32InputDocPaths", this.InputDocPaths);

        return globalConfigBuilder.ToString();

        void AddPathsProperty(string name, ImmutableArray<string> paths)
        {
            if (!paths.IsEmpty)
            {
                globalConfigBuilder.AppendLine($"build_property.{name} = {string.Join("|", paths)}");
            }
        }
    }

    private static ImmutableArray<string> CollectAssemblyMetadata(string name) => [.. typeof(GeneratorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(metadata => metadata.Key == name && metadata.Value is not null).Select(metadata => metadata.Value!)];
}
