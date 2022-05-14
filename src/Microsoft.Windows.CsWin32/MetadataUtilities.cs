// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;

namespace Microsoft.Windows.CsWin32;

internal static class MetadataUtilities
{
    [Flags]
    internal enum InteropArchitecture
    {
#pragma warning disable SA1602 // Enumeration items should be documented
        None = 0x0,
        X86 = 0x1,
        X64 = 0x2,
        Arm64 = 0x4,
        All = 0x7,
#pragma warning restore SA1602 // Enumeration items should be documented
    }

    internal static bool IsCompatibleWithPlatform(MetadataReader mr, MetadataIndex index, Platform? platform, CustomAttributeHandleCollection customAttributesOnMember)
    {
        if (index.SupportedArchitectureAttributeCtor == default)
        {
            // This metadata never uses the SupportedArchitectureAttribute, so we assume this member is compatible.
            return true;
        }

        foreach (CustomAttributeHandle attHandle in customAttributesOnMember)
        {
            CustomAttribute att = mr.GetCustomAttribute(attHandle);
            if (att.Constructor.Equals(index.SupportedArchitectureAttributeCtor))
            {
                if (platform is null)
                {
                    // Without a compilation, we cannot ascertain compatibility.
                    return false;
                }

                var requiredPlatform = (InteropArchitecture)(int)att.DecodeValue(CustomAttributeTypeProvider.Instance).FixedArguments[0].Value!;
                return platform switch
                {
                    Platform.AnyCpu or Platform.AnyCpu32BitPreferred => requiredPlatform == InteropArchitecture.All,
                    Platform.Arm64 => (requiredPlatform & InteropArchitecture.Arm64) == InteropArchitecture.Arm64,
                    Platform.X86 => (requiredPlatform & InteropArchitecture.X86) == InteropArchitecture.X86,
                    Platform.X64 => (requiredPlatform & InteropArchitecture.X64) == InteropArchitecture.X64,
                    _ => false,
                };
            }
        }

        // No SupportedArchitectureAttribute on this member, so assume it is compatible.
        return true;
    }

    internal static bool IsAttribute(MetadataReader reader, CustomAttribute attribute, string ns, string name)
    {
        StringHandle actualNamespace, actualName;
        if (attribute.Constructor.Kind == HandleKind.MemberReference)
        {
            MemberReference memberReference = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            TypeReference parentRef = reader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
            actualNamespace = parentRef.Namespace;
            actualName = parentRef.Name;
        }
        else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            TypeDefinition typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
            actualNamespace = typeDef.Namespace;
            actualName = typeDef.Name;
        }
        else
        {
            throw new NotSupportedException("Unsupported attribute constructor kind: " + attribute.Constructor.Kind);
        }

        return reader.StringComparer.Equals(actualName, name) && reader.StringComparer.Equals(actualNamespace, ns);
    }
}
