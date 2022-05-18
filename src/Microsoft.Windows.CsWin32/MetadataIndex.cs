// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

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
    private static readonly Dictionary<CacheKey, MetadataIndex> Cache = new();

    /// <summary>
    /// A cache of metadata files read.
    /// All access to this should be within a <see cref="Cache"/> lock.
    /// </summary>
    private static readonly Dictionary<string, MemoryMappedFile> MetadataFiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly string metadataPath;

    private readonly Platform? platform;

    private readonly List<TypeDefinitionHandle> apis = new();

    private readonly HashSet<string> releaseMethods = new HashSet<string>(StringComparer.Ordinal);

    private readonly Dictionary<TypeReferenceHandle, TypeDefinitionHandle> refToDefCache = new();

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
    /// Initializes a new instance of the <see cref="MetadataIndex"/> class.
    /// </summary>
    /// <param name="metadataPath">The path to the metadata that this index will represent.</param>
    /// <param name="platform">The platform filter to apply when reading the metadata.</param>
    private MetadataIndex(string metadataPath, Platform? platform)
    {
        this.metadataPath = metadataPath;
        this.platform = platform;

        using PEReader peReader = new PEReader(CreateFileView(metadataPath));
        MetadataReader mr = peReader.GetMetadataReader();

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
                            mr.StringComparer.Equals(tr.Namespace, "Windows.Win32.Interop"))
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
                            foreach (CustomAttributeHandle h in td.GetCustomAttributes())
                            {
                                CustomAttribute att = mr.GetCustomAttribute(h);
                                if (MetadataUtilities.IsAttribute(mr, att, Generator.InteropDecorationNamespace, Generator.RAIIFreeAttribute))
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
                                        if (fieldDef.DecodeSignature(SignatureHandleProvider.Instance, null) is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr })
                                        {
                                            this.handleTypeStructsWithIntPtrSizeFields.Add(typeName);
                                        }
                                    }

                                    break;
                                }
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

    private string DebuggerDisplay => $"{this.metadataPath} ({this.platform})";

    internal static MetadataIndex Get(string metadataPath, Platform? platform)
    {
        metadataPath = Path.GetFullPath(metadataPath);
        CacheKey key = new(metadataPath, platform);
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out MetadataIndex index))
            {
                Cache.Add(key, index = new MetadataIndex(metadataPath, platform));
            }

            return index;
        }
    }

    internal static MemoryMappedViewStream CreateFileView(string metadataPath)
    {
        lock (Cache)
        {
            // We use a memory mapped file so that many threads can perform random access on it concurrently,
            // only mapping the file into memory once.
            if (!MetadataFiles.TryGetValue(metadataPath, out MemoryMappedFile? file))
            {
                var metadataStream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                file = MemoryMappedFile.CreateFromFile(metadataStream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
                MetadataFiles.Add(metadataPath, file);
            }

            return file.CreateViewStream(offset: 0, size: 0, MemoryMappedFileAccess.Read);
        }
    }

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
            foreach (TypeDefinitionHandle tdh in reader.TypeDefinitions)
            {
                TypeDefinition typeDef = reader.GetTypeDefinition(tdh);
                if (typeDef.Name == typeRef.Name && typeDef.Namespace == typeRef.Namespace)
                {
                    if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        // The ref is nested. Verify that the type we found is nested in the same type as well.
                        if (this.TryGetTypeDefHandle(reader, (TypeReferenceHandle)typeRef.ResolutionScope, out TypeDefinitionHandle nestingTypeDef) && nestingTypeDef == typeDef.GetDeclaringType())
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
        }

        this.refToDefCache.Add(typeRefHandle, typeDefHandle);
        return !typeDefHandle.IsNil;
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

    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    private struct CacheKey : IEquatable<CacheKey>
    {
        internal CacheKey(string metadataPath, Platform? platform)
        {
            this.MetadataPath = metadataPath;
            this.Platform = platform;
        }

        internal string MetadataPath { get; }

        internal Platform? Platform { get; }

        private string DebuggerDisplay => $"{this.MetadataPath} ({this.Platform})";

        public override bool Equals(object obj) => obj is CacheKey other && this.Equals(other);

        public bool Equals(CacheKey other)
        {
            return this.Platform == other.Platform
                && string.Equals(this.MetadataPath, other.MetadataPath, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(this.MetadataPath) + (this.Platform.HasValue ? (int)this.Platform.Value : 0);
        }
    }
}
