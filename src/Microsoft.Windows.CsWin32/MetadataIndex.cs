// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Reflection.PortableExecutable;
    using System.Runtime.InteropServices;
    using Microsoft.CodeAnalysis;

    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    internal class MetadataIndex : IDisposable
    {
        private static readonly Dictionary<CacheKey, Stack<MetadataIndex>> Cache = new();

        /// <summary>
        /// A cache of metadata files read.
        /// All access to this should be within a <see cref="Cache"/> lock.
        /// </summary>
        private static readonly Dictionary<string, MemoryMappedFile> MetadataFiles = new(StringComparer.OrdinalIgnoreCase);

        private readonly string metadataPath;

        private readonly Platform? platform;

        private readonly Stream metadataStream;

        private readonly PEReader peReader;

        private readonly MetadataReader mr;

        private readonly List<TypeDefinition> apis = new();

        private readonly HashSet<string> releaseMethods = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The set of names of typedef structs that represent handles where the handle has length of <see cref="IntPtr"/>
        /// and is therefore appropriate to wrap in a <see cref="SafeHandle"/>.
        /// </summary>
        private readonly HashSet<string> handleTypeStructsWithIntPtrSizeFields = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// A dictionary where the key is the typedef struct name and the value is the method used to release it.
        /// </summary>
        private readonly Dictionary<TypeDefinitionHandle, string> handleTypeReleaseMethod = new();

        private MetadataIndex(string metadataPath, Stream metadataStream, Platform? platform)
        {
            this.metadataPath = metadataPath;
            this.platform = platform;

            try
            {
                this.metadataStream = metadataStream;
                this.peReader = new PEReader(this.metadataStream);
                this.mr = this.peReader.GetMetadataReader();

                foreach (MemberReferenceHandle memberRefHandle in this.mr.MemberReferences)
                {
                    MemberReference memberReference = this.mr.GetMemberReference(memberRefHandle);
                    if (memberReference.GetKind() == MemberReferenceKind.Method)
                    {
                        if (memberReference.Parent.Kind == HandleKind.TypeReference)
                        {
                            if (this.mr.StringComparer.Equals(memberReference.Name, ".ctor"))
                            {
                                var trh = (TypeReferenceHandle)memberReference.Parent;
                                TypeReference tr = this.mr.GetTypeReference(trh);
                                if (this.mr.StringComparer.Equals(tr.Name, "SupportedArchitectureAttribute") &&
                                    this.mr.StringComparer.Equals(tr.Namespace, "Windows.Win32.Interop"))
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
                    string nsLeafName = this.mr.GetString(ns.Name);
                    string nsFullName = string.IsNullOrEmpty(parentNamespace) ? nsLeafName : $"{parentNamespace}.{nsLeafName}";

                    var nsMetadata = new NamespaceMetadata(nsFullName);

                    foreach (TypeDefinitionHandle tdh in ns.TypeDefinitions)
                    {
                        TypeDefinition td = this.mr.GetTypeDefinition(tdh);
                        string typeName = this.mr.GetString(td.Name);
                        if (typeName == "Apis")
                        {
                            this.apis.Add(td);
                            foreach (MethodDefinitionHandle methodDefHandle in td.GetMethods())
                            {
                                MethodDefinition methodDef = this.mr.GetMethodDefinition(methodDefHandle);
                                string methodName = this.mr.GetString(methodDef.Name);
                                if (MetadataUtilities.IsCompatibleWithPlatform(this.mr, this, platform, methodDef.GetCustomAttributes()))
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
                                FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldDefHandle);
                                const FieldAttributes expectedFlags = FieldAttributes.Static | FieldAttributes.Public;
                                if ((fieldDef.Attributes & expectedFlags) == expectedFlags)
                                {
                                    string fieldName = this.mr.GetString(fieldDef.Name);
                                    nsMetadata.Fields.Add(fieldName, fieldDefHandle);
                                }
                            }
                        }
                        else if (typeName == "<Module>")
                        {
                        }
                        else if (MetadataUtilities.IsCompatibleWithPlatform(this.mr, this, platform, td.GetCustomAttributes()))
                        {
                            nsMetadata.Types.Add(typeName, tdh);

                            // Detect if this is a struct representing a native handle.
                            if (td.GetFields().Count == 1 && td.BaseType.Kind == HandleKind.TypeReference)
                            {
                                TypeReference baseType = this.mr.GetTypeReference((TypeReferenceHandle)td.BaseType);
                                if (this.mr.StringComparer.Equals(baseType.Name, nameof(ValueType)) && this.mr.StringComparer.Equals(baseType.Namespace, nameof(System)))
                                {
                                    foreach (CustomAttributeHandle h in td.GetCustomAttributes())
                                    {
                                        CustomAttribute att = this.mr.GetCustomAttribute(h);
                                        if (MetadataUtilities.IsAttribute(this.mr, att, Generator.InteropDecorationNamespace, Generator.RAIIFreeAttribute))
                                        {
                                            var args = att.DecodeValue(CustomAttributeTypeProvider.Instance);
                                            if (args.FixedArguments[0].Value is string freeMethodName)
                                            {
                                                this.handleTypeReleaseMethod.Add(tdh, freeMethodName);
                                                this.releaseMethods.Add(freeMethodName);

                                                using FieldDefinitionHandleCollection.Enumerator fieldEnum = td.GetFields().GetEnumerator();
                                                fieldEnum.MoveNext();
                                                FieldDefinitionHandle fieldHandle = fieldEnum.Current;
                                                FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldHandle);
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
                        PopulateNamespace(this.mr.GetNamespaceDefinition(childNsHandle), nsFullName);
                    }
                }

                foreach (NamespaceDefinitionHandle childNsHandle in this.mr.GetNamespaceDefinitionRoot().NamespaceDefinitions)
                {
                    PopulateNamespace(this.mr.GetNamespaceDefinitionRoot(), parentNamespace: null);
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
            catch
            {
                this.peReader?.Dispose();
                this.metadataStream?.Dispose();
                throw;
            }
        }

        internal MetadataReader Reader => this.mr;

        /// <summary>
        /// Gets the ref handle to the constructor on the SupportedArchitectureAttribute, if there is one.
        /// </summary>
        internal MemberReferenceHandle SupportedArchitectureAttributeCtor { get; }

        internal ReadOnlyCollection<TypeDefinition> Apis => new(this.apis);

        /// <summary>
        /// Gets a dictionary of namespace metadata, indexed by the string handle to their namespace.
        /// </summary>
        internal Dictionary<string, NamespaceMetadata> MetadataByNamespace { get; } = new();

        internal IReadOnlyCollection<string> ReleaseMethods => this.releaseMethods;

        internal IReadOnlyDictionary<TypeDefinitionHandle, string> HandleTypeReleaseMethod => this.handleTypeReleaseMethod;

        internal string CommonNamespace { get; }

        internal string CommonNamespaceDot { get; }

        private string DebuggerDisplay => $"{this.metadataPath} ({this.platform})";

        public void Dispose()
        {
            this.peReader.Dispose();
            this.metadataStream.Dispose();
        }

        internal static MetadataIndex Get(string metadataPath, Platform? platform)
        {
            metadataPath = Path.GetFullPath(metadataPath);
            CacheKey key = new CacheKey(metadataPath, platform);
            MemoryMappedViewStream metadataBytes;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out Stack<MetadataIndex> stack) && stack.Count > 0)
                {
                    return stack.Pop();
                }

                // Read the entire metadata file exactly once so that many MemoryStreams can share the memory.
                if (!MetadataFiles.TryGetValue(metadataPath, out MemoryMappedFile? file))
                {
                    FileStream metadataStream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    file = MemoryMappedFile.CreateFromFile(metadataStream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
                    MetadataFiles.Add(metadataPath, file);
                }

                metadataBytes = file.CreateViewStream(offset: 0, size: 0, MemoryMappedFileAccess.Read);
            }

            return new MetadataIndex(metadataPath, metadataBytes, platform);
        }

        internal static void Return(MetadataIndex index)
        {
            CacheKey key = new CacheKey(index.metadataPath, index.platform);
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out Stack<MetadataIndex> stack))
                {
                    Cache.Add(key, stack = new Stack<MetadataIndex>());
                }

                stack.Push(index);
            }
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
}
