// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal abstract record TypeHandleInfo
{
    private static readonly TypeSyntaxSettings DebuggerDisplaySettings = new TypeSyntaxSettings(null, PreferNativeInt: false, PreferMarshaledTypes: false, AllowMarshaling: false, QualifyNames: true);

    internal bool IsConstantField { get; init; }

    [OverloadResolutionPriority(1)]
    internal abstract TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, QualifiedCustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default);

    internal TypeSyntaxAndMarshaling ToTypeSyntax(TypeSyntaxSettings inputs, Generator.GeneratingElement forElement, CustomAttributeHandleCollection? customAttributes, ParameterAttributes parameterAttributes = default)
    {
        QualifiedCustomAttributeHandleCollection? qualifiedCollection = customAttributes is null ? null : new QualifiedCustomAttributeHandleCollection(this.GetGenerator(inputs.Generator) ?? throw new ArgumentException("Generator required."), customAttributes.Value);
        return this.ToTypeSyntax(inputs, forElement, qualifiedCollection, parameterAttributes);
    }

    internal abstract bool? IsValueType(TypeSyntaxSettings inputs);

    internal abstract Generator? GetGenerator(Generator? inputGenerator);

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

    protected TypeSyntax ToTypeSyntaxForDisplay() => this.ToTypeSyntax(DebuggerDisplaySettings, Generator.GeneratingElement.Other, null).Type;

    protected Generator.Context GetContext(TypeSyntaxSettings inputs) => inputs.Generator is not null
        ? inputs.Generator.DefaultContext with { AllowMarshaling = inputs.AllowMarshaling }
        : new() { AllowMarshaling = inputs.AllowMarshaling };
}
