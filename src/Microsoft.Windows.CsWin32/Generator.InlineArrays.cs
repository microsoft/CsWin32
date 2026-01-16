// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? MarshalAsAttribute) DeclareFixedLengthArrayStruct(FieldDefinition fieldDef, CustomAttributeHandleCollection customAttributes, TypeHandleInfo fieldTypeHandleInfo, ArrayTypeSyntax arrayType, Context context)
    {
        if (context.AllowMarshaling && this.IsManagedType(fieldTypeHandleInfo) && !this.useSourceGenerators)
        {
            ArrayTypeSyntax ranklessArray = arrayType.WithRankSpecifiers([ArrayRankSpecifier()]);
            AttributeSyntax marshalAs = MarshalAs(UnmanagedType.ByValArray, sizeConst: arrayType.RankSpecifiers[0].Sizes[0]);
            return (ranklessArray, default(SyntaxList<MemberDeclarationSyntax>), marshalAs);
        }

        int length = int.Parse(((LiteralExpressionSyntax)arrayType.RankSpecifiers[0].Sizes[0]).Token.ValueText, CultureInfo.InvariantCulture);
        TypeSyntax elementType = arrayType.ElementType;

        static string SanitizeTypeName(string typeName) => typeName.Replace(' ', '_').Replace('.', '_').Replace(':', '_').Replace('*', '_').Replace('<', '_').Replace('>', '_').Replace('[', '_').Replace(']', '_').Replace(',', '_');
        void DetermineNames(TypeSyntax elementType, out string? structNamespace, out string fixedLengthStructNameString, out string? fileNamePrefix)
        {
            if (elementType is QualifiedNameSyntax qualifiedElementType)
            {
                structNamespace = qualifiedElementType.Left.ToString();
                if (!structNamespace.StartsWith(GlobalWinmdRootNamespaceAlias))
                {
                    // Force structs to be under the root namespace.
                    structNamespace = GlobalWinmdRootNamespaceAlias;
                }

                fileNamePrefix = SanitizeTypeName(qualifiedElementType.Right.Identifier.ValueText);
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is PredefinedTypeSyntax predefined)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = predefined.Keyword.ValueText;
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is IdentifierNameSyntax identifier)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = identifier.Identifier.ValueText;
                fixedLengthStructNameString = $"__{fileNamePrefix}_{length}";
            }
            else if (elementType is FunctionPointerTypeSyntax functionPtr)
            {
                structNamespace = GlobalWinmdRootNamespaceAlias;
                fileNamePrefix = "FunctionPointer";
                fixedLengthStructNameString = $"__{SanitizeTypeName(functionPtr.ToString())}_{length}";
            }
            else if (elementType is PointerTypeSyntax elementPointerType)
            {
                DetermineNames(elementPointerType.ElementType, out structNamespace, out fixedLengthStructNameString, out fileNamePrefix);
                fixedLengthStructNameString = $"P{fixedLengthStructNameString}";
            }
            else
            {
                throw new NotSupportedException($"Type {elementType} had unexpected kind: {elementType.GetType().Name}");
            }

            // Generate inline array as a nested struct if the element type is itself a nested type.
            if (fieldTypeHandleInfo is ArrayTypeHandleInfo { ElementType: HandleTypeHandleInfo fieldHandleTypeInfo } && this.IsNestedType(fieldHandleTypeInfo.Handle))
            {
                structNamespace = null;
                fileNamePrefix = null;
            }
        }

        DetermineNames(elementType, out string? structNamespace, out string fixedLengthStructNameString, out string? fileNamePrefix);
        IdentifierNameSyntax fixedLengthStructName = IdentifierName(fixedLengthStructNameString);
        TypeSyntax qualifiedFixedLengthStructName = ParseTypeName($"{structNamespace}.{fixedLengthStructNameString}");

        if (structNamespace is not null && this.volatileCode.IsInlineArrayStructGenerated(structNamespace, fixedLengthStructNameString))
        {
            return (qualifiedFixedLengthStructName, default, default);
        }

        // IntPtr/UIntPtr began implementing IEquatable<T> in .NET 5. We may want to actually resolve the type in the compilation to see if it implements this.
        bool elementTypeIsEquatable = elementType is PredefinedTypeSyntax;
        bool fixedArrayAllowed = elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword or (int)SyntaxKind.ByteKeyword or (int)SyntaxKind.ShortKeyword or (int)SyntaxKind.IntKeyword or (int)SyntaxKind.LongKeyword or (int)SyntaxKind.CharKeyword or (int)SyntaxKind.SByteKeyword or (int)SyntaxKind.UShortKeyword or (int)SyntaxKind.UIntKeyword or (int)SyntaxKind.ULongKeyword or (int)SyntaxKind.FloatKeyword or (int)SyntaxKind.DoubleKeyword };

        // internal struct __TheStruct_Count
        // {
        //     internal unsafe fixed TheStruct Value[LENGTH];
        //     /// <summary>The length of the inline array.</summary>
        //     internal const int Length = LENGTH;
        // ...
        IdentifierNameSyntax lengthConstant = IdentifierName("SpanLength");
        IdentifierNameSyntax lengthInstanceProperty = IdentifierName("Length");

        // private const int SpanLength = 8;
        MemberDeclarationSyntax spanLengthDeclaration = FieldDeclaration(
            [TokenWithSpace(SyntaxKind.PrivateKeyword), TokenWithSpace(SyntaxKind.ConstKeyword)],
            VariableDeclaration(
                PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)),
                [VariableDeclarator(lengthConstant.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(length))))]));

        //// internal readonly int Length => SpanLength;
        MemberDeclarationSyntax lengthDeclaration = PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), lengthInstanceProperty.Identifier)
            .WithExpressionBody(ArrowExpressionClause(lengthConstant))
            .WithSemicolonToken(Semicolon)
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
            .WithLeadingTrivia(Trivia(DocumentationCommentTrivia(
                SyntaxKind.SingleLineDocumentationCommentTrivia,
                [
                    DocCommentStart,
                    XmlElement("summary", [XmlText("The length of the inline array.")]),
                    DocCommentEnd
                ])));

        StructDeclarationSyntax? fixedLengthStruct = StructDeclaration(fixedLengthStructName.Identifier, [spanLengthDeclaration, lengthDeclaration])
            .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.PartialKeyword));

        IdentifierNameSyntax? valueFieldName = null;
        IdentifierNameSyntax? firstElementName = null;
        if (fixedArrayAllowed)
        {
            // internal unsafe fixed TheStruct Value[SpanLength];
            valueFieldName = IdentifierName("Value");
            fixedLengthStruct = fixedLengthStruct.AddMembers(
                FieldDeclaration(
                    [TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.FixedKeyword)],
                    VariableDeclaration(elementType, [VariableDeclarator(valueFieldName.Identifier).AddArgumentListArguments(Argument(lengthConstant))])));
        }
        else
        {
            // internal TheStruct _0, _1, _2, ...;
            firstElementName = IdentifierName("_0");
            FieldDeclarationSyntax fieldDecl = FieldDeclaration([TokenWithSpace(this.Visibility)], VariableDeclaration(elementType, [.. Enumerable.Range(0, length).Select(i => VariableDeclarator(Identifier(Invariant($"_{i}"))))]));
            if (RequiresUnsafe(elementType))
            {
                fieldDecl = fieldDecl.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(fieldDecl);
        }

        // fixed (TheStruct* p0 = Value) ...
        // - or - (depending on fixed array field use)
        // fixed (TheStruct* p0 = &_0) ...
        FixedStatementSyntax FixedBlock(SyntaxToken pointerLocalIdentifier, StatementSyntax body) =>
            FixedStatement(
                VariableDeclaration(PointerType(elementType), [VariableDeclarator(pointerLocalIdentifier, EqualsValueClause((ExpressionSyntax?)valueFieldName ?? PrefixUnaryExpression(SyntaxKind.AddressOfExpression, firstElementName!)))]),
                body);

        if (valueFieldName is not null)
        {
            // [UnscopedRef] internal unsafe ref TheStruct this[int index] => ref Value[index];
            IndexerDeclarationSyntax indexer = IndexerDeclaration(RefType(elementType).WithTrailingTrivia(TriviaList(Space)), [Parameter(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), Identifier("index"))])
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .WithExpressionBody(ArrowExpressionClause(RefExpression(
                    ElementAccessExpression(valueFieldName, [Argument(IdentifierName("index"))]))))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeIndexerComment);
            fixedLengthStruct = fixedLengthStruct.AddMembers(indexer);
            this.DeclareUnscopedRefAttributeIfNecessary();
        }
        else
        {
            // internal unsafe char this[int index]
            ////readonly get
            ////{
            ////    fixed (char* p0 = &_0)
            ////        return new Span<char>(p0, SpanLength)[index];
            ////}

            ////set
            ////{
            ////    fixed (char* p0 = &_0)
            ////        new Span<char>(p0, SpanLength)[index] = value;
            ////}
        }

        IdentifierNameSyntax? asReadOnlyMethodName = null;

        // Pointers cannot be used as type arguments, so if the element type is unsafe (a pointer), we have to skip the Span<T> methods.
        // We could overcome this by defining a PElementType struct that contains the pointer, then use the PElementType as the type argument.
        if (this.canCallCreateSpan && !RequiresUnsafe(elementType))
        {
            // Value[0]
            ExpressionSyntax value0 = valueFieldName is not null
                ? ElementAccessExpression(valueFieldName, [Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)))])
                : firstElementName!;

            // ref Value[0]
            ArgumentSyntax refValue0 = Argument(nameColon: null, TokenWithSpace(SyntaxKind.RefKeyword), value0);

            // MemoryMarshal.CreateSpan(ref Value[0], Length)
            InvocationExpressionSyntax createSpanInvocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("MemoryMarshal"), IdentifierName("CreateSpan")),
                FixTrivia(ArgumentList(refValue0, Argument(lengthConstant))));

            // [UnscopedRef] internal unsafe Span<TheStruct> AsSpan() => MemoryMarshal.CreateSpan(ref Value[0], Length);
            MethodDeclarationSyntax asSpanMethod = MethodDeclaration(MakeSpanOfT(elementType).WithTrailingTrivia(TriviaList(Space)), Identifier("AsSpan"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .WithExpressionBody(ArrowExpressionClause(createSpanInvocation))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeAsSpanComment);
            this.DeclareUnscopedRefAttributeIfNecessary();

            // ref Unsafe.AsRef(Value[0])
            ArgumentSyntax refUnsafeValue0 = Argument(
                nameColon: null,
                TokenWithSpace(SyntaxKind.RefKeyword),
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), IdentifierName(nameof(Unsafe.AsRef))),
                    [Argument(value0).WithRefKindKeyword(Token(SyntaxKind.InKeyword))]));

            // MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(Value[0]), Length)
            InvocationExpressionSyntax createReadOnlySpanInvocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("MemoryMarshal"), IdentifierName("CreateReadOnlySpan")),
                FixTrivia(ArgumentList(refUnsafeValue0, Argument(lengthConstant))));

            // [UnscopedRef] internal unsafe readonly ReadOnlySpan<TheStruct> AsReadOnlySpan() => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(Value[0]), Length);
            asReadOnlyMethodName = IdentifierName("AsReadOnlySpan");
            MethodDeclarationSyntax asReadOnlySpanMethod = MethodDeclaration(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(TriviaList(Space)), asReadOnlyMethodName.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.UnsafeKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .WithExpressionBody(ArrowExpressionClause(createReadOnlySpanInvocation))
                .WithSemicolonToken(SemicolonWithLineFeed)
                .AddAttributeLists(AttributeList(UnscopedRefAttributeSyntax))
                .WithLeadingTrivia(InlineArrayUnsafeAsSpanComment);

            fixedLengthStruct = fixedLengthStruct.AddMembers(asSpanMethod, asReadOnlySpanMethod);
        }

#pragma warning disable SA1515 // Single-line comment should be preceded by blank line
#pragma warning disable SA1114 // Parameter list should follow declaration

        bool generateSpanLegacyHelpers = this.canUseSpan && (!this.canCallCreateSpan || this.Options.MultiTargetingFriendlyAPIs);
        if (generateSpanLegacyHelpers && !RequiresUnsafe(elementType))
        {
            // internal readonly void CopyTo(Span<TheStruct> target, int length = Length)
            IdentifierNameSyntax targetParameterName = IdentifierName("target");
            IdentifierNameSyntax lengthParameterName = IdentifierName("length");
            IdentifierNameSyntax copyToMethodName = IdentifierName("CopyTo");
            MethodDeclarationSyntax copyToMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), copyToMethodName.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(MakeSpanOfT(elementType).WithTrailingTrivia(Space), targetParameterName.Identifier),
                    Parameter(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), lengthParameterName.Identifier).WithDefault(EqualsValueClause(lengthConstant)));

            // x.Slice(0, length).CopyTo(target)
            InvocationExpressionSyntax CopyToExpression(ExpressionSyntax readOnlySpanExpression) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(ReadOnlySpan<>.Slice))),
                            [Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))), Argument(lengthParameterName)]),
                        IdentifierName(nameof(ReadOnlySpan<>.CopyTo))),
                    [Argument(targetParameterName)]);

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).CopyTo(target);
                copyToMethod = copyToMethod
                    .WithExpressionBody(ArrowExpressionClause(CopyToExpression(InvocationExpression(asReadOnlyMethodName))))
                    .WithSemicolonToken(SemicolonWithLineFeed);
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                copyToMethod = copyToMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                        //// fixed (TheStruct* p0 = Value) new ReadOnlySpan<char>(p0, Length).Slice(0, length).CopyTo(target);
                        FixedBlock(
                            p0Local.Identifier,
                            ExpressionStatement(CopyToExpression(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType), [Argument(p0Local), Argument(lengthConstant)]))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(copyToMethod);

            // internal readonly TheStruct[] ToArray(int length = Length)
            MethodDeclarationSyntax toArrayMethod = MethodDeclaration(ArrayType(elementType, [ArrayRankSpecifier()]), Identifier("ToArray"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), lengthParameterName.Identifier).WithDefault(EqualsValueClause(lengthConstant)));

            // x.Slice(0, length).ToArray()
            InvocationExpressionSyntax ToArrayExpression(ExpressionSyntax readOnlySpanExpression) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(ReadOnlySpan<>.Slice))),
                            [Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))), Argument(lengthParameterName)]),
                        IdentifierName(nameof(ReadOnlySpan<>.ToArray))));

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).ToArray()
                toArrayMethod = toArrayMethod
                    .WithExpressionBody(ArrowExpressionClause(ToArrayExpression(InvocationExpression(asReadOnlyMethodName))))
                    .WithSemicolonToken(SemicolonWithLineFeed);
                if (RequiresUnsafe(elementType))
                {
                    toArrayMethod = toArrayMethod.AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword));
                }
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toArrayMethod = toArrayMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                        //// fixed (TheStruct* p0 = Value)
                        FixedBlock(
                            p0Local.Identifier,
                            //// return new ReadOnlySpan<char>(p0, Length).Slice(0, length).ToArray();
                            ReturnStatement(ToArrayExpression(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType), [Argument(p0Local), Argument(lengthConstant)]))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toArrayMethod);
        }

        if (this.canUseSpan && elementTypeIsEquatable)
        {
            // internal readonly bool Equals(ReadOnlySpan<TheStruct> value)
            IdentifierNameSyntax valueParameterName = IdentifierName("value");
            MethodDeclarationSyntax equalsSpanMethod = MethodDeclaration(PredefinedType(Token(SyntaxKind.BoolKeyword)), Identifier(nameof(object.Equals)))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(Space), valueParameterName.Identifier));

            ExpressionSyntax EqualsBoolExpression(ExpressionSyntax readOnlySpanExpression) => elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.CharKeyword }
                ? ConditionalExpression(
                    //// value.Length == Length
                    BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameterName, IdentifierName(nameof(ReadOnlySpan<>.Length))),
                        lengthConstant),
                    //// span.SequenceEqual(value)
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(Enumerable.SequenceEqual))),
                        [Argument(valueParameterName)]),
                    //// span.SliceAtNull().SequenceEqual(value)
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, SliceAtNullMethodName)),
                            IdentifierName(nameof(Enumerable.SequenceEqual))),
                        [Argument(valueParameterName)]))
                : InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpanExpression, IdentifierName(nameof(Enumerable.SequenceEqual))),
                        [Argument(valueParameterName)]); // span.SequenceEqual(value);
            this.DeclareSliceAtNullExtensionMethodIfNecessary();

            if (asReadOnlyMethodName is not null)
            {
                // => value.Length == Length ? AsReadOnlySpan().SequenceEqual(value) : AsReadOnlySpan().SliceAtNull().SequenceEqual(value);
                equalsSpanMethod = equalsSpanMethod
                    .WithExpressionBody(ArrowExpressionClause(EqualsBoolExpression(InvocationExpression(asReadOnlyMethodName))))
                    .WithSemicolonToken(Semicolon);
            }
            else
            {
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                IdentifierNameSyntax spanLocal = IdentifierName("span");
                equalsSpanMethod = equalsSpanMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                        // fixed (TheStruct* p0 = Value)
                        FixedBlock(
                            p0Local.Identifier,
                            Block(
                                // ReadOnlySpan<char> span = new(p0, Length);
                                LocalDeclarationStatement(VariableDeclaration(
                                    MakeReadOnlySpanOfT(elementType),
                                    [
                                        VariableDeclarator(spanLocal.Identifier, EqualsValueClause(
                                            ObjectCreationExpression(MakeReadOnlySpanOfT(elementType), [Argument(p0Local), Argument(lengthConstant)])))
                                    ])),
                                // return value.Length == Length ? span.SequenceEqual(value) : span.SliceAtNull().SequenceEqual(value);
                                ReturnStatement(EqualsBoolExpression(spanLocal))))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(equalsSpanMethod);
        }

#pragma warning restore SA1114 // Parameter list should follow declaration
#pragma warning restore SA1515 // Single-line comment should be preceded by blank line

        if (elementType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.CharKeyword })
        {
            if (this.canUseSpan)
            {
                // internal readonly bool Equals(string value) => Equals(value.AsSpan());
                fixedLengthStruct = fixedLengthStruct.AddMembers(
                    MethodDeclaration(PredefinedType(Token(SyntaxKind.BoolKeyword)), Identifier("Equals"))
                        .AddModifiers(Token(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                        .AddParameterListParameters(Parameter(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)), Identifier("value")))
                        .WithExpressionBody(ArrowExpressionClause(InvocationExpression(
                            IdentifierName("Equals"),
                            [Argument(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("value"), IdentifierName("AsSpan"))))])))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // internal unsafe readonly string ToString(int length)
            IdentifierNameSyntax lengthParameterName = IdentifierName("length");
            MethodDeclarationSyntax toStringLengthMethod =
                MethodDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)), Identifier(nameof(this.ToString)))
                    .AddModifiers(Token(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .AddParameterListParameters(
                        Parameter(PredefinedType(Token(SyntaxKind.IntKeyword)).WithTrailingTrivia(Space), lengthParameterName.Identifier))
                    .WithLeadingTrivia(InlineCharArrayToStringWithLengthComment);

            // x.Slice(0, length).ToString()
            InvocationExpressionSyntax SliceAtLengthToString(ExpressionSyntax readOnlySpan) =>
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpan, IdentifierName("Slice")),
                            [
                                Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                Argument(lengthParameterName)
                            ]),
                        IdentifierName(nameof(this.ToString))));

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().Slice(0, length).ToString()
                toStringLengthMethod = toStringLengthMethod
                    .WithExpressionBody(ArrowExpressionClause(SliceAtLengthToString(InvocationExpression(asReadOnlyMethodName))))
                    .WithSemicolonToken(Semicolon);
            }
            else if (this.canUseSpan)
            {
                // fixed (char* p0 = Value) return new ReadOnlySpan<char>(p0, Length).Slice(0, length).ToString();
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringLengthMethod = toStringLengthMethod
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                        FixedBlock(
                            p0Local.Identifier,
                            ReturnStatement(SliceAtLengthToString(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType), [Argument(p0Local), Argument(lengthConstant)]))))));
            }
            else
            {
                // if (length < 0 || length > Length)
                //     throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be between 0 and the fixed array length.");
                // fixed (char* p0 = this.Value)
                //     return new string(p0, 0, length);
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringLengthMethod = toStringLengthMethod
                     .AddModifiers(Token(SyntaxKind.UnsafeKeyword))
                     .WithBody(Block(
                         IfStatement(
                             BinaryExpression(
                                 SyntaxKind.LogicalOrExpression,
                                 BinaryExpression(SyntaxKind.LessThanExpression, lengthParameterName, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                 BinaryExpression(SyntaxKind.GreaterThanExpression, lengthParameterName, lengthConstant)),
                             ThrowStatement(ObjectCreationExpression(
                                 IdentifierName(nameof(ArgumentOutOfRangeException)),
                                 [
                                     Argument(NameOfExpression(lengthParameterName)),
                                     Argument(lengthParameterName),
                                     Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Length must be between 0 and the fixed array length, inclusive.")))
                                 ]))),
                         FixedBlock(
                             p0Local.Identifier,
                             ReturnStatement(
                                 ObjectCreationExpression(
                                     PredefinedType(Token(SyntaxKind.StringKeyword)),
                                     [
                                         Argument(p0Local),
                                         Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                                         Argument(lengthParameterName)
                                     ])))));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toStringLengthMethod);

            // public override readonly string ToString()
            MethodDeclarationSyntax toStringOverride =
                MethodDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)), Identifier(nameof(this.ToString)))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.OverrideKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword))
                    .WithLeadingTrivia(InlineCharArrayToStringComment);

            // x.SliceAtNull().ToString()
            InvocationExpressionSyntax SliceAtNullToString(ExpressionSyntax readOnlySpan)
            {
                this.DeclareSliceAtNullExtensionMethodIfNecessary();
                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, readOnlySpan, SliceAtNullMethodName)),
                        IdentifierName(nameof(object.ToString))));
            }

            if (asReadOnlyMethodName is not null)
            {
                // => AsReadOnlySpan().SliceAtNull().ToString();
                toStringOverride = toStringOverride
                    .WithExpressionBody(ArrowExpressionClause(SliceAtNullToString(InvocationExpression(asReadOnlyMethodName))))
                    .WithSemicolonToken(Semicolon);
            }
            else if (this.canUseSpan)
            {
                // fixed (char* p0 = Value) return new ReadOnlySpan<char>(p0, Length).SliceAtNull().ToString();
                IdentifierNameSyntax p0Local = IdentifierName("p0");
                toStringOverride = toStringOverride
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                        FixedBlock(
                            p0Local.Identifier,
                            ReturnStatement(SliceAtNullToString(ObjectCreationExpression(MakeReadOnlySpanOfT(elementType), [Argument(p0Local), Argument(lengthConstant)]))))));
            }
            else
            {
                IdentifierNameSyntax lengthLocalVar = IdentifierName("length");
                StatementSyntax[] lengthDeclarationStatements;

                // int length;
                // fixed (char* p = Value)
                // {
                //     char* pLastExclusive = p + Length;
                //     char* pCh = p;
                //     for (; pCh < pLastExclusive && *pCh != '\0'; pCh++);
                //     length = checked((int)(pCh - p));
                // }
                IdentifierNameSyntax p = IdentifierName("p");
                IdentifierNameSyntax pLastExclusive = IdentifierName("pLastExclusive");
                IdentifierNameSyntax pCh = IdentifierName("pCh");
                lengthDeclarationStatements = new StatementSyntax[]
                {
                    LocalDeclarationStatement(VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)), [VariableDeclarator(lengthLocalVar.Identifier)])),
                    FixedBlock(
                        p.Identifier,
                        Block(
                            LocalDeclarationStatement(VariableDeclaration(
                                PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                                [VariableDeclarator(pLastExclusive.Identifier, EqualsValueClause(BinaryExpression(SyntaxKind.AddExpression, p, IdentifierName("Length"))))])),
                            LocalDeclarationStatement(VariableDeclaration(
                                PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                                [VariableDeclarator(pCh.Identifier, EqualsValueClause(p))])),
                            ForStatement(
                                null,
                                BinaryExpression(
                                        SyntaxKind.LogicalAndExpression,
                                        BinaryExpression(
                                            SyntaxKind.LessThanExpression,
                                            pCh,
                                            pLastExclusive),
                                        BinaryExpression(
                                            SyntaxKind.NotEqualsExpression,
                                            PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, pCh),
                                            LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')))),
                                [PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, pCh)],
                                EmptyStatement()),
                            ExpressionStatement(AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                lengthLocalVar,
                                CheckedExpression(CastExpression(
                                    PredefinedType(Token(SyntaxKind.IntKeyword)),
                                    ParenthesizedExpression(BinaryExpression(SyntaxKind.SubtractExpression, pCh, p)))))))),
                };

                // return ToString(length);
                toStringOverride = toStringOverride
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .WithBody(Block(
                    [
                        .. lengthDeclarationStatements,
                        ReturnStatement(InvocationExpression(
                            IdentifierName("ToString"),
                            [Argument(lengthLocalVar)]))
                    ]));
            }

            fixedLengthStruct = fixedLengthStruct.AddMembers(toStringOverride);

            if (this.canUseSpan)
            {
                // public static implicit operator __char_64(string? value) => value.AsSpan();
                fixedLengthStruct = fixedLengthStruct.AddMembers(
                    ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fixedLengthStructName)
                        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                        .AddParameterListParameters(Parameter(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)), Identifier("value")))
                        .WithExpressionBody(ArrowExpressionClause(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("value"), IdentifierName(nameof(MemoryExtensions.AsSpan))))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // Make sure .NET marshals these `char` arrays as UTF-16.
            fixedLengthStruct = fixedLengthStruct
                .AddAttributeLists(AttributeList(StructLayout(TypeAttributes.SequentialLayout, charSet: CharSet.Unicode)));
        }

        // public static implicit operator __TheStruct_64(ReadOnlySpan<TheStruct> value)
        if (this.canUseSpan && !RequiresUnsafe(elementType))
        {
            IdentifierNameSyntax valueParam = IdentifierName("value");
            ConversionOperatorDeclarationSyntax implicitSpanToStruct =
                ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fixedLengthStructName)
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                    .AddParameterListParameters(Parameter(MakeReadOnlySpanOfT(elementType).WithTrailingTrivia(TriviaList(Space)), valueParam.Identifier))
                    .WithBody(Block());

            IdentifierNameSyntax resultLocal = IdentifierName("result");
            IdentifierNameSyntax initLengthLocal = IdentifierName("initLength");

            ExpressionSyntax firstElement = valueFieldName is not null
                ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, valueFieldName) // result.Value
                : PrefixUnaryExpression(SyntaxKind.AddressOfExpression, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, firstElementName!)); // &result._0

            if (this.canUseUnsafeSkipInit)
            {
                // Unsafe.SkipInit(out __char_1 result);
                implicitSpanToStruct = implicitSpanToStruct.AddBodyStatements(
                    ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(Unsafe)), IdentifierName("SkipInit")),
                        [Argument(nameColon: null, Token(SyntaxKind.OutKeyword), DeclarationExpression(fixedLengthStructName.WithTrailingTrivia(Space), SingleVariableDesignation(resultLocal.Identifier)))])));
            }
            else
            {
                implicitSpanToStruct = implicitSpanToStruct.AddBodyStatements(
                    LocalDeclarationStatement(VariableDeclaration(fixedLengthStructName, [VariableDeclarator(resultLocal.Identifier, EqualsValueClause(DefaultExpression(fixedLengthStructName)))])));
            }

            // x.Slice(initLength, Length - initLength).Clear();
            StatementSyntax ClearSlice(ExpressionSyntax span) =>
                ExpressionStatement(InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, span, IdentifierName(nameof(Span<>.Slice))),
                            [
                                Argument(initLengthLocal),
                                Argument(BinaryExpression(SyntaxKind.SubtractExpression, lengthConstant, initLengthLocal))
                            ]),
                        IdentifierName(nameof(Span<>.Clear)))));

            if (this.canUseSpan)
            {
                //// int initLength = value.Length;
                LocalDeclarationStatementSyntax declareInitLength =
                    LocalDeclarationStatement(VariableDeclaration(
                        PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)),
                        [VariableDeclarator(initLengthLocal.Identifier, EqualsValueClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<>.Length)))))]));

                if (this.canCallCreateSpan)
                {
                    // value.CopyTo(result.AsSpan());
                    StatementSyntax valueCopyToResult =
                        ExpressionStatement(InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<>.CopyTo))),
                            [Argument(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, IdentifierName("AsSpan"))))]));
                    implicitSpanToStruct = implicitSpanToStruct
                        .AddBodyStatements(
                            valueCopyToResult,
                            declareInitLength,
                            //// result.AsSpan().Slice(initLength, Length - initLength).Clear();
                            ClearSlice(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, IdentifierName("AsSpan")))));
                }
                else
                {
                    IdentifierNameSyntax targetLocal = IdentifierName("target");

                    // Span<char> target = new Span<char>(result.Value, Length);
                    StatementSyntax declareTargetLocal =
                        LocalDeclarationStatement(VariableDeclaration(
                            MakeSpanOfT(elementType),
                            [VariableDeclarator(targetLocal.Identifier, EqualsValueClause(ObjectCreationExpression(MakeSpanOfT(elementType), [Argument(firstElement), Argument(lengthConstant)])))]));

                    implicitSpanToStruct = implicitSpanToStruct
                        .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                        .AddBodyStatements(
                            declareTargetLocal,
                            ////value.CopyTo(target);
                            ExpressionStatement(InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<>.CopyTo))),
                                [Argument(targetLocal)])),
                            declareInitLength,
                            ////target.Slice(initLength, Length - initLength).Clear();
                            ClearSlice(targetLocal));
                }
            }
            else
            {
                IdentifierNameSyntax pLocal = IdentifierName("p");
                IdentifierNameSyntax iLocal = IdentifierName("i");

                // if (value.Length > result.Length) throw new ArgumentException("Too long");
                StatementSyntax checkRange = IfStatement(
                    BinaryExpression(
                        SyntaxKind.GreaterThanExpression,
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<>.Length))),
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, resultLocal, lengthConstant)),
                    ThrowStatement(ObjectCreationExpression(IdentifierName(nameof(ArgumentException)), [Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Length exceeds fixed array size.")))])));

                implicitSpanToStruct = implicitSpanToStruct
                    .AddModifiers(TokenWithSpace(SyntaxKind.UnsafeKeyword))
                    .AddBodyStatements(
                        checkRange,
                        //// TheStruct* p = result.Value;
                        LocalDeclarationStatement(VariableDeclaration(
                            PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                            [VariableDeclarator(pLocal.Identifier, EqualsValueClause(firstElement))])),
                        //// for (int i = 0; i < value.Length; i++) *p++ = value[i];
                        ForStatement(
                            VariableDeclaration(
                                PredefinedType(Token(SyntaxKind.IntKeyword)),
                                [VariableDeclarator(iLocal.Identifier, EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))]),
                            BinaryExpression(SyntaxKind.LessThanExpression, iLocal, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParam, IdentifierName(nameof(ReadOnlySpan<>.Length)))),
                            [PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, iLocal)],
                            Block(
                                ExpressionStatement(AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, pLocal)),
                                    ElementAccessExpression(valueParam, [Argument(iLocal)]))))));
            }

            // return result;
            implicitSpanToStruct = implicitSpanToStruct.AddBodyStatements(ReturnStatement(resultLocal));

            fixedLengthStruct = fixedLengthStruct
                .AddMembers(implicitSpanToStruct);
        }

        // internal static unsafe ref readonly TheStruct ReadOnlyItemRef(this in MainAVIHeader.__dwReserved_4 @this, int index)
        if (valueFieldName is not null)
        {
            IdentifierNameSyntax indexParamName = IdentifierName("index");
            IdentifierNameSyntax atThis = IdentifierName("@this");
            TypeSyntax qualifiedElementType;
            if (elementType == IntPtrTypeSyntax)
            {
                qualifiedElementType = elementType;
            }
            else
            {
                qualifiedElementType = fieldTypeHandleInfo.ToTypeSyntax(this.extensionMethodSignatureTypeSettings, GeneratingElement.Other, customAttributes.QualifyWith(this)).Type switch
                {
                    ArrayTypeSyntax at => at.ElementType,
                    PointerTypeSyntax ptrType => ptrType.ElementType,
                    _ => throw new GenerationFailedException($"Unexpected runtime type."),
                };
            }

            TypeSyntaxSettings extensionMethodSignatureTypeSettings = context.Filter(this.extensionMethodSignatureTypeSettings);

            // internal static unsafe ref readonly TheStruct ReadOnlyItemRef(this in MainAVIHeader.__dwReserved_4 @this, int index) => ref @this.Value[index]
            ParameterSyntax thisParameter = Parameter(qualifiedFixedLengthStructName.WithTrailingTrivia(Space), atThis.Identifier)
                .AddModifiers(TokenWithSpace(SyntaxKind.ThisKeyword), TokenWithSpace(SyntaxKind.InKeyword));
            ParameterSyntax indexParameter = Parameter(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), indexParamName.Identifier);
            MethodDeclarationSyntax getAtMethod = MethodDeclaration(RefType(qualifiedElementType.WithTrailingTrivia(TriviaList(Space))).WithReadOnlyKeyword(TokenWithSpace(SyntaxKind.ReadOnlyKeyword)), Identifier("ReadOnlyItemRef"))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.UnsafeKeyword))
                .WithParameterList(FixTrivia(ParameterList([thisParameter, indexParameter])))
                .WithExpressionBody(ArrowExpressionClause(RefExpression(ElementAccessExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, atThis, valueFieldName), [Argument(indexParamName)]))))
                .WithSemicolonToken(Semicolon);

            this.volatileCode.AddInlineArrayIndexerExtension(getAtMethod);
        }

        if (structNamespace is not null)
        {
            // Wrap with any additional namespaces.
            MemberDeclarationSyntax fixedLengthStructInNamespace = fixedLengthStruct;
            if (structNamespace != GlobalWinmdRootNamespaceAlias)
            {
                if (!structNamespace.StartsWith(GlobalWinmdRootNamespaceAlias + ".", StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"The {structNamespace}.{fixedLengthStructNameString} struct must be under the metadata's common namespace.");
                }

                fixedLengthStructInNamespace = NamespaceDeclaration(ParseName(structNamespace.Substring(GlobalWinmdRootNamespaceAlias.Length + 1)), [fixedLengthStruct]);
            }

            fixedLengthStructInNamespace = fixedLengthStructInNamespace
                    .WithAdditionalAnnotations(new SyntaxAnnotation(SimpleFileNameAnnotation, $"{fileNamePrefix}.InlineArrays"));

            this.volatileCode.AddInlineArrayStruct(structNamespace, fixedLengthStructNameString, fixedLengthStructInNamespace);

            return (qualifiedFixedLengthStructName, default, null);
        }
        else
        {
            // This struct will be injected as a nested type, to match the element type.
            return (fixedLengthStructName, [fixedLengthStruct], null);
        }
    }

    private ClassDeclarationSyntax DeclareInlineArrayIndexerExtensionsClass()
    {
        var filteredExtensionMethods =
            this.committedCode.InlineArrayIndexerExtensions.Where(e =>
                this.FindExtensionMethodIfAlreadyAvailable($"{this.Namespace}.{InlineArrayIndexerExtensionsClassName}", e.Identifier.ValueText) is null);

        return ClassDeclaration(InlineArrayIndexerExtensionsClassName.Identifier, [.. filteredExtensionMethods])
            .WithModifiers([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.PartialKeyword)])
            .AddAttributeLists(AttributeList(GeneratedCodeAttribute));
    }
}
