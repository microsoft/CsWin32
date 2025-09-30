// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private class GeneratedCode
    {
        private readonly GeneratedCode? parent;

        private readonly Dictionary<string, List<MemberDeclarationSyntax>> modulesAndMembers = new Dictionary<string, List<MemberDeclarationSyntax>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The structs, enums, delegates and other supporting types for extern methods.
        /// </summary>
        private readonly Dictionary<(TypeDefinitionHandle Type, bool HasUnmanagedName), MemberDeclarationSyntax> types = new();

        private readonly Dictionary<FieldDefinitionHandle, (FieldDeclarationSyntax FieldDeclaration, TypeDefinitionHandle? FieldType)> fieldsToSyntax = new();

        private readonly List<ClassDeclarationSyntax> safeHandleTypes = new();

        private readonly Dictionary<string, (MemberDeclarationSyntax Type, bool TopLevel)> specialTypes = new(StringComparer.Ordinal);

        private readonly Dictionary<string, MethodDeclarationSyntax> macros = new(StringComparer.Ordinal);

        private readonly Dictionary<string, CustomMarshalerTypeRecord> customTypeMarshalers = new();

        /// <summary>
        /// The set of types that are or have been generated so we don't stack overflow for self-referencing types.
        /// </summary>
        private readonly Dictionary<(TypeDefinitionHandle, bool), Exception?> typesGenerating = new();

        /// <summary>
        /// The set of methods that are or have been generated.
        /// </summary>
        private readonly Dictionary<MethodDefinitionHandle, Exception?> methodsGenerating = new();

        /// <summary>
        /// The constants that are or have been generated.
        /// </summary>
        private readonly Dictionary<FieldDefinitionHandle, Exception?> constantsGenerating = new();

        /// <summary>
        /// A collection of the names of special types we are or have generated.
        /// </summary>
        private readonly Dictionary<string, Exception?> specialTypesGenerating = new(StringComparer.Ordinal);

        private readonly Dictionary<(string Namespace, string Name), MemberDeclarationSyntax> inlineArrays = new();

        private readonly Dictionary<string, TypeSyntax?> releaseMethodsWithSafeHandleTypesGenerating = new();

        private readonly List<MethodDeclarationSyntax> inlineArrayIndexerExtensionsMembers = new();

        private readonly List<ClassDeclarationSyntax> comInterfaceFriendlyExtensionsMembers = new();

        private bool generating;

        internal GeneratedCode()
        {
        }

        internal GeneratedCode(GeneratedCode parent)
        {
            this.parent = parent;
        }

        internal bool IsEmpty => this.modulesAndMembers.Count == 0 && this.types.Count == 0 && this.fieldsToSyntax.Count == 0 && this.safeHandleTypes.Count == 0 && this.specialTypes.Count == 0
            && this.inlineArrayIndexerExtensionsMembers.Count == 0 && this.comInterfaceFriendlyExtensionsMembers.Count == 0 && this.macros.Count == 0 && this.inlineArrays.Count == 0 && this.customTypeMarshalers.Count == 0;

        internal bool NeedsWinRTCustomMarshaler { get; private set; }

        internal IEnumerable<MemberDeclarationSyntax> GeneratedTypes => this.GetTypesWithInjectedFields()
            .Concat(this.specialTypes.Values.Where(st => !st.TopLevel).Select(st => st.Type))
            .Concat(this.safeHandleTypes)
            .Concat(this.inlineArrays.Values)
            .Concat(this.customTypeMarshalers.Values.Select(x => x.ClassDeclaration));

        internal IEnumerable<MemberDeclarationSyntax> GeneratedTopLevelTypes => this.specialTypes.Values.Where(st => st.TopLevel).Select(st => st.Type);

        internal IReadOnlyCollection<ClassDeclarationSyntax> ComInterfaceExtensions => this.comInterfaceFriendlyExtensionsMembers;

        internal IEnumerable<MethodDeclarationSyntax> InlineArrayIndexerExtensions => this.inlineArrayIndexerExtensionsMembers;

        internal IEnumerable<FieldDeclarationSyntax> TopLevelFields => from field in this.fieldsToSyntax.Values
                                                                       where field.FieldType is null || !this.types.ContainsKey((field.FieldType.Value, false))
                                                                       select field.FieldDeclaration;

        internal IEnumerable<IGrouping<string, MemberDeclarationSyntax>> MembersByModule
        {
            get
            {
                foreach (KeyValuePair<string, List<MemberDeclarationSyntax>> item in this.modulesAndMembers)
                {
                    yield return new Grouping<string, MemberDeclarationSyntax>(item.Key, item.Value);
                }
            }
        }

        internal IEnumerable<MethodDeclarationSyntax> Macros => this.macros.Values;

        internal void AddSafeHandleType(ClassDeclarationSyntax safeHandleDeclaration)
        {
            this.ThrowIfNotGenerating();

            this.safeHandleTypes.Add(safeHandleDeclaration);
        }

        internal void AddMemberToModule(string moduleName, MemberDeclarationSyntax member)
        {
            this.ThrowIfNotGenerating();

            if (!this.modulesAndMembers.TryGetValue(moduleName, out List<MemberDeclarationSyntax>? methodsList))
            {
                this.modulesAndMembers.Add(moduleName, methodsList = new List<MemberDeclarationSyntax>());
            }

            methodsList.Add(member);
            this.NeedsWinRTCustomMarshaler |= RequiresWinRTCustomMarshaler(member);
        }

        internal void AddMemberToModule(string moduleName, IEnumerable<MemberDeclarationSyntax> members)
        {
            this.ThrowIfNotGenerating();

            if (!this.modulesAndMembers.TryGetValue(moduleName, out List<MemberDeclarationSyntax>? methodsList))
            {
                this.modulesAndMembers.Add(moduleName, methodsList = new List<MemberDeclarationSyntax>());
            }

            methodsList.AddRange(members);
            this.NeedsWinRTCustomMarshaler |= members.Any(m => RequiresWinRTCustomMarshaler(m));
        }

        internal void AddConstant(FieldDefinitionHandle fieldDefHandle, FieldDeclarationSyntax constantDeclaration, TypeDefinitionHandle? fieldType)
        {
            this.ThrowIfNotGenerating();
            this.fieldsToSyntax.Add(fieldDefHandle, (constantDeclaration, fieldType));
        }

        internal void AddMacro(string macroName, MethodDeclarationSyntax macro)
        {
            this.ThrowIfNotGenerating();
            this.macros.Add(macroName, macro);
        }

        internal void AddCustomTypeMarshaler(string marshalerName, CustomMarshalerTypeRecord typeRecord)
        {
            this.ThrowIfNotGenerating();
            this.customTypeMarshalers.Add(marshalerName, typeRecord);
        }

        internal void AddInlineArrayIndexerExtension(MethodDeclarationSyntax inlineIndexer)
        {
            this.ThrowIfNotGenerating();

            string thisParameter = inlineIndexer.ParameterList.Parameters[0].Type!.ToString();

            IEnumerable<MethodDeclarationSyntax> toSearch = this.inlineArrayIndexerExtensionsMembers;
            if (this.parent is not null)
            {
                toSearch = toSearch.Concat(this.parent.inlineArrayIndexerExtensionsMembers);
            }

            if (!toSearch.Any(m => m.Identifier.ValueText == inlineIndexer.Identifier.ValueText && m.ParameterList.Parameters[0].Type!.ToString() == thisParameter))
            {
                this.inlineArrayIndexerExtensionsMembers.Add(inlineIndexer);
            }
        }

        internal void AddComInterfaceExtension(ClassDeclarationSyntax extension)
        {
            this.ThrowIfNotGenerating();
            this.comInterfaceFriendlyExtensionsMembers.Add(extension);
        }

        internal void AddComInterfaceExtension(IEnumerable<ClassDeclarationSyntax> extension)
        {
            this.ThrowIfNotGenerating();
            this.comInterfaceFriendlyExtensionsMembers.AddRange(extension);
        }

        /// <summary>
        /// Adds a declaration to the generated code.
        /// </summary>
        /// <param name="specialName">The same constant provided to <see cref="GenerateSpecialType(string, Action)"/>. This serves to avoid repeat declarations.</param>
        /// <param name="specialDeclaration">The declaration.</param>
        /// <param name="topLevel"><see langword="true" /> if this declaration should <em>not</em> be nested within the top-level namespace for generated code.</param>
        internal void AddSpecialType(string specialName, MemberDeclarationSyntax specialDeclaration, bool topLevel = false)
        {
            this.ThrowIfNotGenerating();
            this.specialTypes.Add(specialName, (specialDeclaration, topLevel));
        }

        internal void AddInteropType(TypeDefinitionHandle typeDefinitionHandle, bool hasUnmanagedName, MemberDeclarationSyntax typeDeclaration)
        {
            this.ThrowIfNotGenerating();
            this.types.Add((typeDefinitionHandle, hasUnmanagedName), typeDeclaration);
            this.NeedsWinRTCustomMarshaler |= RequiresWinRTCustomMarshaler(typeDeclaration);
        }

        internal void GenerationTransaction(Action generator)
        {
            if (this.parent is null)
            {
                throw new InvalidOperationException("Code generation should occur in a volatile instance.");
            }

            if (this.generating)
            {
                // A transaction is already running. Just run the generator.
                generator();
                return;
            }

            try
            {
                this.generating = true;
                generator();
                this.Commit(this.parent);
            }
            catch
            {
                this.Commit(null);
                throw;
            }
            finally
            {
                this.generating = false;
            }
        }

        internal void GenerateMethod(MethodDefinitionHandle methodDefinitionHandle, Action generator)
        {
            this.ThrowIfNotGenerating();

            if (this.methodsGenerating.TryGetValue(methodDefinitionHandle, out Exception? failure) || this.parent?.methodsGenerating.TryGetValue(methodDefinitionHandle, out failure) is true)
            {
                if (failure is object)
                {
                    throw new GenerationFailedException("This member already failed in generation previously.", failure);
                }

                return;
            }

            this.methodsGenerating.Add(methodDefinitionHandle, null);
            try
            {
                generator();
            }
            catch (Exception ex)
            {
                this.methodsGenerating[methodDefinitionHandle] = ex;
                throw;
            }
        }

        internal void GenerateSpecialType(string name, Action generator)
        {
            this.ThrowIfNotGenerating();

            if (this.specialTypesGenerating.TryGetValue(name, out Exception? failure) || this.parent?.specialTypesGenerating.TryGetValue(name, out failure) is true)
            {
                if (failure is object)
                {
                    throw new GenerationFailedException("This type already failed in generation previously.", failure);
                }

                return;
            }

            this.specialTypesGenerating.Add(name, null);
            try
            {
                generator();
            }
            catch (Exception ex)
            {
                this.specialTypesGenerating[name] = ex;
                throw;
            }
        }

        internal bool IsInlineArrayStructGenerated(string @namespace, string name) => this.parent?.inlineArrays.ContainsKey((@namespace, name)) is true || this.inlineArrays.ContainsKey((@namespace, name));

        internal void AddInlineArrayStruct(string @namespace, string name, MemberDeclarationSyntax inlineArrayStructDeclaration)
        {
            this.ThrowIfNotGenerating();

            this.inlineArrays.Add((@namespace, name), inlineArrayStructDeclaration);
        }

        internal void GenerateType(TypeDefinitionHandle typeDefinitionHandle, bool hasUnmanagedName, Action generator)
        {
            this.ThrowIfNotGenerating();

            var key = (typeDefinitionHandle, hasUnmanagedName);
            if (this.typesGenerating.TryGetValue(key, out Exception? failure) || this.parent?.typesGenerating.TryGetValue(key, out failure) is true)
            {
                if (failure is object)
                {
                    throw new GenerationFailedException("This type already failed in generation previously.", failure);
                }

                return;
            }

            this.typesGenerating.Add(key, null);
            try
            {
                generator();
            }
            catch (Exception ex)
            {
                this.typesGenerating[key] = ex;
                throw;
            }
        }

        internal void GenerateConstant(FieldDefinitionHandle fieldDefinitionHandle, Action generator)
        {
            this.ThrowIfNotGenerating();

            if (this.constantsGenerating.TryGetValue(fieldDefinitionHandle, out Exception? failure) || this.parent?.constantsGenerating.TryGetValue(fieldDefinitionHandle, out failure) is true)
            {
                if (failure is object)
                {
                    throw new GenerationFailedException("This constant already failed in generation previously.", failure);
                }

                return;
            }

            this.constantsGenerating.Add(fieldDefinitionHandle, null);
            try
            {
                generator();
            }
            catch (Exception ex)
            {
                this.constantsGenerating[fieldDefinitionHandle] = ex;
                throw;
            }
        }

        internal void GenerateMacro(string macroName, Action generator)
        {
            this.ThrowIfNotGenerating();

            if (this.macros.ContainsKey(macroName) || this.parent?.macros.ContainsKey(macroName) is true)
            {
                return;
            }

            generator();
        }

        internal string GenerateCustomTypeMarshaler(string marshalerName, Func<CustomMarshalerTypeRecord> generator)
        {
            this.ThrowIfNotGenerating();

            if (this.customTypeMarshalers.TryGetValue(marshalerName, out CustomMarshalerTypeRecord? marshalerType) ||
                (this.parent?.customTypeMarshalers.TryGetValue(marshalerName, out marshalerType) ?? false))
            {
                // Result is in marshalerType
            }
            else
            {
                marshalerType = generator();
            }

            return marshalerType.QualifiedName;
        }

        internal bool TryGetSafeHandleForReleaseMethod(string releaseMethod, out TypeSyntax? safeHandleType)
        {
            return this.releaseMethodsWithSafeHandleTypesGenerating.TryGetValue(releaseMethod, out safeHandleType)
                || this.parent?.releaseMethodsWithSafeHandleTypesGenerating.TryGetValue(releaseMethod, out safeHandleType) is true;
        }

        internal void AddSafeHandleNameForReleaseMethod(string releaseMethod, TypeSyntax? safeHandleType)
        {
            this.ThrowIfNotGenerating();

            this.releaseMethodsWithSafeHandleTypesGenerating.Add(releaseMethod, safeHandleType);
        }

        private static void Commit<TKey, TValue>(Dictionary<TKey, TValue> source, Dictionary<TKey, TValue>? target)
            where TKey : notnull
        {
            if (target is object)
            {
                foreach (KeyValuePair<TKey, TValue> item in source)
                {
                    target.Add(item.Key, item.Value);
                }
            }

            source.Clear();
        }

        private static void Commit<T>(List<T> source, List<T>? target)
        {
            if (target is object)
            {
                target.AddRange(source);
            }

            source.Clear();
        }

        private static bool RequiresWinRTCustomMarshaler(SyntaxNode node)
            => node.DescendantNodesAndSelf().OfType<AttributeSyntax>()
                .Any(a => a.Name.ToString() == "MarshalAs" && a.ToString().Contains(WinRTCustomMarshalerFullName));

        private void Commit(GeneratedCode? parent)
        {
            foreach (KeyValuePair<string, List<MemberDeclarationSyntax>> item in this.modulesAndMembers)
            {
                if (parent is object)
                {
                    if (!parent.modulesAndMembers.TryGetValue(item.Key, out List<MemberDeclarationSyntax>? list))
                    {
                        parent.modulesAndMembers.Add(item.Key, list = new());
                    }

                    list.AddRange(item.Value);
                }

                item.Value.Clear();
            }

            Commit(this.types, parent?.types);
            Commit(this.fieldsToSyntax, parent?.fieldsToSyntax);
            Commit(this.safeHandleTypes, parent?.safeHandleTypes);
            Commit(this.specialTypes, parent?.specialTypes);
            Commit(this.typesGenerating, parent?.typesGenerating);
            Commit(this.macros, parent?.macros);
            Commit(this.methodsGenerating, parent?.methodsGenerating);
            Commit(this.specialTypesGenerating, parent?.specialTypesGenerating);
            Commit(this.inlineArrays, parent?.inlineArrays);
            Commit(this.releaseMethodsWithSafeHandleTypesGenerating, parent?.releaseMethodsWithSafeHandleTypesGenerating);
            Commit(this.inlineArrayIndexerExtensionsMembers, parent?.inlineArrayIndexerExtensionsMembers);
            Commit(this.comInterfaceFriendlyExtensionsMembers, parent?.comInterfaceFriendlyExtensionsMembers);
            Commit(this.customTypeMarshalers, parent?.customTypeMarshalers);

            if (parent is not null)
            {
                parent.NeedsWinRTCustomMarshaler |= this.NeedsWinRTCustomMarshaler;
            }

            this.NeedsWinRTCustomMarshaler = false;
        }

        private IEnumerable<MemberDeclarationSyntax> GetTypesWithInjectedFields()
        {
#pragma warning disable CS8714
            var fieldsByType =
                (from field in this.fieldsToSyntax
                 where field.Value.FieldType is not null
                 group field.Value.FieldDeclaration by field.Value.FieldType into typeGroup
                 select typeGroup).ToDictionary(k => k.Key!, k => k.ToArray());
            foreach (var pair in this.types)
            {
                MemberDeclarationSyntax type = pair.Value;
                if (fieldsByType.TryGetValue(pair.Key.Type, out var extraFields))
                {
                    switch (type)
                    {
                        case StructDeclarationSyntax structType:
                            type = structType.AddMembers(extraFields);
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                }

                yield return type;
            }
#pragma warning restore CS8714
        }

        private void ThrowIfNotGenerating()
        {
            if (!this.generating)
            {
                throw new InvalidOperationException("Generating code must take place within a recognized top-level call.");
            }
        }

        private class Grouping<TKey, TElement> : IGrouping<TKey, TElement>
        {
            private readonly IEnumerable<TElement> values;

            internal Grouping(TKey key, IEnumerable<TElement> values)
            {
                this.Key = key;
                this.values = values;
            }

            public TKey Key { get; }

            public IEnumerator<TElement> GetEnumerator() => this.values.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
        }
    }
}
