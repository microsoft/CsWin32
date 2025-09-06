// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// A cached, shareable index into a particular metadata file.
/// </summary>
/// <devremarks>
/// This class <em>must not</em> store anything to do with a <see cref="MetadataReader"/>,
/// since that is attached to a stream which will not allow for concurrent use.
/// This means we cannot store definitions (e.g. <see cref="TypeDefinition"/>)
/// because they store the <see cref="MetadataReader"/> as a field.
/// We can store handles though (e.g. <see cref="TypeDefinitionHandle"/>, since
/// the only thing they store is an index into the metadata, which is constant across
/// <see cref="MetadataReader"/> instances for a given metadata file.
/// </devremarks>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
internal class MetadataIndex
{
    private static readonly int MaxPooledObjectCount = Math.Max(Environment.ProcessorCount, 4);

    private readonly MetadataFile metadataFile;

    private readonly Platform? platform;

    private readonly List<TypeDefinitionHandle> apis = new();

    private readonly HashSet<string> releaseMethods = new HashSet<string>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<TypeReferenceHandle, TypeDefinitionHandle> refToDefCache = new();

    /// <summary>
    /// The set of names of typedef structs that represent handles where the handle has length of <see cref="IntPtr"/>
    /// and is therefore appropriate to wrap in a <see cref="SafeHandle"/>.
    /// </summary>
    private readonly HashSet<string> handleTypeStructsWithIntPtrSizeFields = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// A dictionary where the key is the typedef struct name and the value is the method used to release it.
    /// </summary>
    private readonly Dictionary<TypeDefinitionHandle, string> handleTypeReleaseMethod = new();

    /// <summary>
    /// A cache kept by the <see cref="TryGetEnumName"/> method.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> enumValueLookupCache = new(StringComparer.Ordinal);

    /// <summary>
    /// A lazily computed reference to System.Enum, as defined by this metadata.
    /// Should be retrieved by <see cref="FindEnumTypeReference(MetadataReader)"/>.
    /// </summary>
    private TypeReferenceHandle? enumTypeReference;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataIndex"/> class.
    /// </summary>
    /// <param name="metadataFile">The metadata file that this index will represent.</param>
    /// <param name="platform">The platform filter to apply when reading the metadata.</param>
    internal MetadataIndex(MetadataFile metadataFile, Platform? platform)
    {
        this.metadataFile = metadataFile;
        this.platform = platform;

        using MetadataFile.Rental mrRental = metadataFile.GetMetadataReader();
        MetadataReader mr = mrRental.Value;
        this.MetadataName = Path.GetFileNameWithoutExtension(mr.GetString(mr.GetAssemblyDefinition().Name));

        foreach (MemberReferenceHandle memberRefHandle in mr.MemberReferences)
        {
            MemberReference memberReference = mr.GetMemberReference(memberRefHandle);
            if (memberReference.GetKind() == MemberReferenceKind.Method)
            {
                if (memberReference.Parent.Kind == HandleKind.TypeReference)
                {
                    if (mr.StringComparer.Equals(memberReference.Name, ".ctor"))
                    {
                        var trh = (TypeReferenceHandle)memberReference.Parent;
                        TypeReference tr = mr.GetTypeReference(trh);
                        if (mr.StringComparer.Equals(tr.Name, "SupportedArchitectureAttribute") &&
                            mr.StringComparer.Equals(tr.Namespace, Generator.InteropDecorationNamespace))
                        {
                            this.SupportedArchitectureAttributeCtor = memberRefHandle;
                            break;
                        }
                    }
                }
            }
        }

        void PopulateNamespace(NamespaceDefinition ns, string? parentNamespace)
        {
            string nsLeafName = mr.GetString(ns.Name);
            string nsFullName = string.IsNullOrEmpty(parentNamespace) ? nsLeafName : $"{parentNamespace}.{nsLeafName}";

            var nsMetadata = new NamespaceMetadata(nsFullName);

            foreach (TypeDefinitionHandle tdh in ns.TypeDefinitions)
            {
                TypeDefinition td = mr.GetTypeDefinition(tdh);
                string typeName = mr.GetString(td.Name);
                if (typeName == "Apis")
                {
                    this.apis.Add(tdh);
                    foreach (MethodDefinitionHandle methodDefHandle in td.GetMethods())
                    {
                        MethodDefinition methodDef = mr.GetMethodDefinition(methodDefHandle);
                        string methodName = mr.GetString(methodDef.Name);
                        if (MetadataUtilities.IsCompatibleWithPlatform(mr, this, platform, methodDef.GetCustomAttributes()))
                        {
                            nsMetadata.Methods.Add(methodName, methodDefHandle);
                        }
                        else
                        {
                            nsMetadata.MethodsForOtherPlatform.Add(methodName);
                        }
                    }

                    foreach (FieldDefinitionHandle fieldDefHandle in td.GetFields())
                    {
                        FieldDefinition fieldDef = mr.GetFieldDefinition(fieldDefHandle);
                        const FieldAttributes expectedFlags = FieldAttributes.Static | FieldAttributes.Public;
                        if ((fieldDef.Attributes & expectedFlags) == expectedFlags)
                        {
                            string fieldName = mr.GetString(fieldDef.Name);
                            nsMetadata.Fields.Add(fieldName, fieldDefHandle);
                        }
                    }
                }
                else if (typeName == "<Module>")
                {
                }
                else if (MetadataUtilities.IsCompatibleWithPlatform(mr, this, platform, td.GetCustomAttributes()))
                {
                    nsMetadata.Types.Add(typeName, tdh);

                    // Detect if this is a struct representing a native handle.
                    if (td.GetFields().Count == 1 && td.BaseType.Kind == HandleKind.TypeReference)
                    {
                        TypeReference baseType = mr.GetTypeReference((TypeReferenceHandle)td.BaseType);
                        if (mr.StringComparer.Equals(baseType.Name, nameof(ValueType)) && mr.StringComparer.Equals(baseType.Namespace, nameof(System)))
                        {
                            if (MetadataUtilities.FindAttribute(mr, td.GetCustomAttributes(), Generator.InteropDecorationNamespace, Generator.RAIIFreeAttribute) is CustomAttribute att)
                            {
                                CustomAttributeValue<CodeAnalysis.CSharp.Syntax.TypeSyntax> args = att.DecodeValue(CustomAttributeTypeProvider.Instance);
                                if (args.FixedArguments[0].Value is string freeMethodName)
                                {
                                    this.handleTypeReleaseMethod.Add(tdh, freeMethodName);
                                    this.releaseMethods.Add(freeMethodName);

                                    using FieldDefinitionHandleCollection.Enumerator fieldEnum = td.GetFields().GetEnumerator();
                                    fieldEnum.MoveNext();
                                    FieldDefinitionHandle fieldHandle = fieldEnum.Current;
                                    FieldDefinition fieldDef = mr.GetFieldDefinition(fieldHandle);
                                    if (fieldDef.DecodeSignature(metadataFile.Generator.SignatureHandleProvider, null) is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr })
                                    {
                                        this.handleTypeStructsWithIntPtrSizeFields.Add(typeName);
                                    }
                                }
                            }
                            else if (this.MetadataName == "Windows.Win32" && typeName == "HGDIOBJ")
                            {
                                // This "base type" struct doesn't have an RAIIFree attribute,
                                // but methods that take an HGDIOBJ parameter are expected to offer SafeHandle friendly overloads.
                                this.handleTypeReleaseMethod.Add(tdh, "DeleteObject");
                            }
                        }
                    }
                }
                else
                {
                    nsMetadata.TypesForOtherPlatform.Add(typeName);
                }
            }

            if (!nsMetadata.IsEmpty)
            {
                this.MetadataByNamespace.Add(nsFullName, nsMetadata);
            }

            foreach (NamespaceDefinitionHandle childNsHandle in ns.NamespaceDefinitions)
            {
                PopulateNamespace(mr.GetNamespaceDefinition(childNsHandle), nsFullName);
            }
        }

        foreach (NamespaceDefinitionHandle childNsHandle in mr.GetNamespaceDefinitionRoot().NamespaceDefinitions)
        {
            PopulateNamespace(mr.GetNamespaceDefinitionRoot(), parentNamespace: null);
        }

        this.CommonNamespace = CommonPrefix(this.MetadataByNamespace.Keys.ToList());
        if (this.CommonNamespace[this.CommonNamespace.Length - 1] == '.')
        {
            this.CommonNamespaceDot = this.CommonNamespace;
            this.CommonNamespace = this.CommonNamespace.Substring(0, this.CommonNamespace.Length - 1);
        }
        else
        {
            this.CommonNamespaceDot = this.CommonNamespace + ".";
        }
    }

    /// <summary>
    /// Gets the assembly name of the metadata file.
    /// </summary>
    internal string MetadataName { get; }

    /// <summary>
    /// Gets the ref handle to the constructor on the SupportedArchitectureAttribute, if there is one.
    /// </summary>
    internal MemberReferenceHandle SupportedArchitectureAttributeCtor { get; }

    /// <summary>
    /// Gets the "Apis" classes across all namespaces.
    /// </summary>
    internal ReadOnlyCollection<TypeDefinitionHandle> Apis => new(this.apis);

    /// <summary>
    /// Gets a dictionary of namespace metadata, indexed by the string handle to their namespace.
    /// </summary>
    internal Dictionary<string, NamespaceMetadata> MetadataByNamespace { get; } = new();

    internal IReadOnlyCollection<string> ReleaseMethods => this.releaseMethods;

    internal IReadOnlyDictionary<TypeDefinitionHandle, string> HandleTypeReleaseMethod => this.handleTypeReleaseMethod;

    internal string CommonNamespace { get; }

    internal string CommonNamespaceDot { get; }

    private string DebuggerDisplay => $"{this.metadataFile.Path} ({this.platform})";

    /// <summary>
    /// Attempts to translate a <see cref="TypeReferenceHandle"/> to a <see cref="TypeDefinitionHandle"/>.
    /// </summary>
    /// <param name="reader">The metadata reader to use.</param>
    /// <param name="typeRefHandle">The reference handle.</param>
    /// <param name="typeDefHandle">Receives the type def handle, if one was discovered.</param>
    /// <returns><see langword="true"/> if a TypeDefinition was found; otherwise <see langword="false"/>.</returns>
    internal bool TryGetTypeDefHandle(MetadataReader reader, TypeReferenceHandle typeRefHandle, out TypeDefinitionHandle typeDefHandle)
    {
        if (this.refToDefCache.TryGetValue(typeRefHandle, out typeDefHandle))
        {
            return !typeDefHandle.IsNil;
        }

        TypeReference typeRef = reader.GetTypeReference(typeRefHandle);
        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            TypeDefinitionHandle expectedNestingTypeDef = default;
            bool foundNestingTypeDef = false;
            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                foundNestingTypeDef = this.TryGetTypeDefHandle(reader, (TypeReferenceHandle)typeRef.ResolutionScope, out expectedNestingTypeDef);
            }

            bool foundPlatformIncompatibleMatch = false;
            foreach (TypeDefinitionHandle tdh in reader.TypeDefinitions)
            {
                TypeDefinition typeDef = reader.GetTypeDefinition(tdh);
                if (typeDef.Name == typeRef.Name && typeDef.Namespace == typeRef.Namespace)
                {
                    if (!MetadataUtilities.IsCompatibleWithPlatform(reader, this, this.platform, typeDef.GetCustomAttributes()))
                    {
                        foundPlatformIncompatibleMatch = true;
                        continue;
                    }

                    if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        // The ref is nested. Verify that the type we found is nested in the same type as well.
                        TypeDefinitionHandle actualNestingType = typeDef.GetDeclaringType();
                        if (foundNestingTypeDef && expectedNestingTypeDef == actualNestingType)
                        {
                            typeDefHandle = tdh;
                            break;
                        }
                    }
                    else if (typeRef.ResolutionScope.Kind == HandleKind.ModuleDefinition && typeDef.GetDeclaringType().IsNil)
                    {
                        typeDefHandle = tdh;
                        break;
                    }
                    else
                    {
                        throw new NotSupportedException("Unrecognized ResolutionScope: " + typeRef.ResolutionScope);
                    }
                }
            }

            if (foundPlatformIncompatibleMatch && typeDefHandle.IsNil)
            {
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                throw new PlatformIncompatibleException($"{ns}.{name} is not declared for this platform.");
            }
        }

        this.refToDefCache.TryAdd(typeRefHandle, typeDefHandle);
        return !typeDefHandle.IsNil;
    }

    /// <summary>
    /// Gets the name of the declaring enum if a supplied value matches the name of an enum's value.
    /// </summary>
    /// <param name="reader">A metadata reader that can be used to fulfill this query.</param>
    /// <param name="enumValueName">A string that may match an enum value name.</param>
    /// <param name="declaringEnum">Receives the name of the declaring enum if a match is found.</param>
    /// <returns><see langword="true"/> if a match was found; otherwise <see langword="false"/>.</returns>
    internal bool TryGetEnumName(MetadataReader reader, string enumValueName, [NotNullWhen(true)] out string? declaringEnum)
    {
        if (this.enumValueLookupCache.TryGetValue(enumValueName, out declaringEnum))
        {
            return declaringEnum is not null;
        }

        // First find the type reference for System.Enum
        TypeReferenceHandle? enumTypeRefHandle = this.FindEnumTypeReference(reader);
        if (enumTypeRefHandle is null)
        {
            // No enums -> it couldn't be what the caller is looking for.
            // This is a quick enough check that we don't need to cache individual inputs/outputs when nothing will produce results for this metadata.
            declaringEnum = null;
            return false;
        }

        foreach (TypeDefinitionHandle typeDefHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (typeDef.BaseType.IsNil)
            {
                continue;
            }

            if (typeDef.BaseType.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var baseTypeHandle = (TypeReferenceHandle)typeDef.BaseType;
            if (!baseTypeHandle.Equals(enumTypeRefHandle.Value))
            {
                continue;
            }

            foreach (FieldDefinitionHandle fieldDefHandle in typeDef.GetFields())
            {
                FieldDefinition fieldDef = reader.GetFieldDefinition(fieldDefHandle);
                if (reader.StringComparer.Equals(fieldDef.Name, enumValueName))
                {
                    declaringEnum = reader.GetString(typeDef.Name);

                    this.enumValueLookupCache[enumValueName] = declaringEnum;

                    return true;
                }
            }
        }

        this.enumValueLookupCache[enumValueName] = null;
        declaringEnum = null;
        return false;
    }

    private static string CommonPrefix(IReadOnlyList<string> ss)
    {
        if (ss.Count == 0)
        {
            return string.Empty;
        }

        if (ss.Count == 1)
        {
            return ss[0];
        }

        int prefixLength = 0;

        foreach (char c in ss[0])
        {
            foreach (string s in ss)
            {
                if (s.Length <= prefixLength || s[prefixLength] != c)
                {
                    return ss[0].Substring(0, prefixLength);
                }
            }

            prefixLength++;
        }

        return ss[0]; // all strings identical up to length of ss[0]
    }

    /// <summary>
    /// Gets the <see cref="TypeReferenceHandle"/> by which the <see cref="Enum"/> class in referenced by this metadata.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <returns>The <see cref="TypeReferenceHandle"/> if a reference to <see cref="Enum"/> was found; otherwise <see langword="null" />.</returns>
    private TypeReferenceHandle? FindEnumTypeReference(MetadataReader reader)
    {
        if (!this.enumTypeReference.HasValue)
        {
            foreach (TypeReferenceHandle typeRefHandle in reader.TypeReferences)
            {
                TypeReference typeRef = reader.GetTypeReference(typeRefHandle);
                if (reader.StringComparer.Equals(typeRef.Name, nameof(Enum)) && reader.StringComparer.Equals(typeRef.Namespace, nameof(System)))
                {
                    this.enumTypeReference = typeRefHandle;
                    break;
                }
            }

            if (!this.enumTypeReference.HasValue)
            {
                // Record that there isn't one.
                this.enumTypeReference = default(TypeReferenceHandle);
            }
        }

        // Return null if the value was determined to be missing.
        return this.enumTypeReference.HasValue && !this.enumTypeReference.Value.IsNil ? this.enumTypeReference.Value : null;
    }
}
