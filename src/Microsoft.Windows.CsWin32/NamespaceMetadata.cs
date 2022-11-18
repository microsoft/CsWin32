// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// An immutable index into metadata.
/// </summary>
/// <devremarks>
/// This class must not contain definitions. It may contain handles. See <see cref="MetadataIndex"/> devremarks for details.
/// </devremarks>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
internal class NamespaceMetadata
{
    internal NamespaceMetadata(string name)
    {
        this.Name = name;
    }

    public string Name { get; }

    public bool IsEmpty => this.Fields.Count == 0 && this.Methods.Count == 0 && this.Types.Count == 0;

    internal Dictionary<string, FieldDefinitionHandle> Fields { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, MethodDefinitionHandle> Methods { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, TypeDefinitionHandle> Types { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> MethodsForOtherPlatform { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal HashSet<string> TypesForOtherPlatform { get; } = new HashSet<string>(StringComparer.Ordinal);

    private string DebuggerDisplay => $"{this.Name} (Constants: {this.Fields.Count}, Methods: {this.Methods.Count}, Types: {this.Types.Count}";
}
