// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;
    using System.Reflection.Metadata;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal abstract record TypeHandleInfo
    {
        private static readonly TypeSyntaxSettings DebuggerDisplaySettings = new TypeSyntaxSettings(null, PreferNativeInt: false, PreferMarshaledTypes: false, UseComInterfaces: false, QualifyNames: true);

        internal abstract TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default);

        protected static bool TryGetSimpleName(TypeSyntax nameSyntax, [NotNullWhen(true)] out string? simpleName)
        {
            if (nameSyntax is QualifiedNameSyntax qname)
            {
                simpleName = qname.Right.Identifier.ValueText;
            }
            else if (nameSyntax is SimpleNameSyntax simple)
            {
                simpleName = simple.Identifier.ValueText;
            }
            else
            {
                simpleName = null;
                return false;
            }

            return true;
        }

        protected TypeSyntax ToTypeSyntaxForDisplay() => this.ToTypeSyntax(DebuggerDisplaySettings, null).Type;
    }
}
