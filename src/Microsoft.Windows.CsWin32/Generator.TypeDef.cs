// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal bool TryGetTypeDefFieldType(TypeHandleInfo? typeDef, [NotNullWhen(true)] out TypeHandleInfo? fieldType)
    {
        if (typeDef is HandleTypeHandleInfo handle)
        {
            switch (handle.Handle.Kind)
            {
                case HandleKind.TypeReference:
                    if (this.TryGetTypeDefHandle((TypeReferenceHandle)handle.Handle, out TypeDefinitionHandle tdh))
                    {
                        return Resolve(tdh, out fieldType);
                    }

                    break;
                case HandleKind.TypeDefinition:
                    return Resolve((TypeDefinitionHandle)handle.Handle, out fieldType);
            }
        }

        bool Resolve(TypeDefinitionHandle tdh, [NotNullWhen(true)] out TypeHandleInfo? fieldType)
        {
            TypeDefinition td = this.Reader.GetTypeDefinition(tdh);
            foreach (FieldDefinitionHandle fdh in td.GetFields())
            {
                FieldDefinition fd = this.Reader.GetFieldDefinition(fdh);
                fieldType = fd.DecodeSignature(this.SignatureHandleProvider, null);
                return true;
            }

            fieldType = null;
            return false;
        }

        fieldType = default;
        return false;
    }

    private bool IsTypeDefStruct(TypeHandleInfo? typeHandleInfo)
    {
        if (typeHandleInfo is HandleTypeHandleInfo handleInfo)
        {
            if (handleInfo.Handle.Kind == HandleKind.TypeDefinition)
            {
                TypeDefinition typeDef = this.Reader.GetTypeDefinition((TypeDefinitionHandle)handleInfo.Handle);
                return this.IsTypeDefStruct(typeDef);
            }
            else if (handleInfo.Handle.Kind == HandleKind.TypeReference)
            {
                if (this.TryGetTypeDefHandle((TypeReferenceHandle)handleInfo.Handle, out TypeDefinitionHandle tdh))
                {
                    TypeDefinition typeDef = this.Reader.GetTypeDefinition(tdh);
                    return this.IsTypeDefStruct(typeDef);
                }
                else if (this.SuperGenerator is object)
                {
                    TypeReference typeReference = this.Reader.GetTypeReference((TypeReferenceHandle)handleInfo.Handle);
                    if (this.SuperGenerator.TryGetTargetGenerator(new QualifiedTypeReference(this, typeReference), out Generator? targetGenerator))
                    {
                        if (targetGenerator.TryGetTypeDefHandle(this.Reader.GetString(typeReference.Namespace), this.Reader.GetString(typeReference.Name), out TypeDefinitionHandle foreignTypeDefHandle))
                        {
                            TypeDefinition foreignTypeDef = targetGenerator.Reader.GetTypeDefinition(foreignTypeDefHandle);
                            return targetGenerator.IsTypeDefStruct(foreignTypeDef);
                        }
                    }
                }
            }
        }
        else if (SpecialTypeDefNames.Contains(null!/*TODO*/))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a struct that emulates a typedef in the C language headers.
    /// </summary>
    private StructDeclarationSyntax DeclareTypeDefStruct(TypeDefinition typeDef, TypeDefinitionHandle typeDefHandle)
    {
        IdentifierNameSyntax name = IdentifierName(this.Reader.GetString(typeDef.Name));
        bool isHandle = this.IsHandle(typeDefHandle, out string? freeMethodName);
        if (freeMethodName is not null)
        {
            this.TryGenerateExternMethod(freeMethodName, out _);
        }

        TypeSyntaxSettings typeSettings = isHandle ? this.fieldOfHandleTypeDefTypeSettings : this.fieldTypeSettings;

        FieldDefinition fieldDef = this.Reader.GetFieldDefinition(typeDef.GetFields().Single());
        string fieldName = this.Reader.GetString(fieldDef.Name);
        IdentifierNameSyntax fieldIdentifierName = SafeIdentifierName(fieldName);
        VariableDeclaratorSyntax fieldDeclarator = VariableDeclarator(fieldIdentifierName.Identifier);
        CustomAttributeHandleCollection fieldAttributes = fieldDef.GetCustomAttributes();
        TypeHandleInfo fieldTypeInfo = fieldDef.DecodeSignature(this.SignatureHandleProvider, null);
        TypeSyntaxAndMarshaling fieldType = fieldTypeInfo.ToTypeSyntax(typeSettings, GeneratingElement.Field, fieldAttributes.QualifyWith(this));
        (TypeSyntax FieldType, SyntaxList<MemberDeclarationSyntax> AdditionalMembers, AttributeSyntax? _) fieldInfo =
            this.ReinterpretFieldType(fieldDef, fieldType.Type, fieldAttributes, this.DefaultContext);
        SyntaxList<MemberDeclarationSyntax> members =
        [
            FieldDeclaration([TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.ReadOnlyKeyword)], VariableDeclaration(fieldType.Type, [fieldDeclarator])),
            .. this.CreateCommonTypeDefMembers(name, fieldType.Type, fieldIdentifierName)
        ];

        IdentifierNameSyntax valueParameter = IdentifierName("value");
        MemberAccessExpressionSyntax fieldAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), fieldIdentifierName);

        if (isHandle && this.IsSafeHandleCompatibleTypeDefFieldType(fieldTypeInfo) && fieldTypeInfo is not PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr })
        {
            // Handle types must interop with IntPtr for SafeHandle support, so if IntPtr isn't the field type,
            // we need to create new conversion operators.
            ExpressionSyntax valueValueArg = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldIdentifierName);
            if (fieldTypeInfo is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.UIntPtr })
            {
                valueValueArg = CastExpression(PredefinedType(TokenWithSpace(SyntaxKind.ULongKeyword)), valueValueArg);

                // We still need to make conversion from an IntPtr simple since so much code relies on it.
                // public static explicit operator SOCKET(IntPtr value) => new SOCKET((UIntPtr)unchecked((ulong)value.ToInt64()));
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                        name,
                        [Argument(CastExpression(fieldInfo.FieldType, UncheckedExpression(CastExpression(PredefinedType(Token(SyntaxKind.ULongKeyword)), InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToInt64))))))))])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }
            else if (fieldType.Type is not IdentifierNameSyntax { Identifier: { Text: nameof(IntPtr) } })
            {
                // public static implicit operator IntPtr(MSIHANDLE value) => new IntPtr(value.Value);
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), IntPtrTypeSyntax)
                    .AddParameterListParameters(Parameter(name.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(
                        ObjectCreationExpression(IntPtrTypeSyntax, [Argument(valueValueArg)])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }

            if (fieldInfo.FieldType is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.UIntKeyword } })
            {
                // public static explicit operator MSIHANDLE(IntPtr value) => new MSIHANDLE((uint)value.ToInt32());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                        name,
                        [
                            Argument(
                                CastExpression(
                                    fieldInfo.FieldType,
                                    InvocationExpression(
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToInt32))))))
                        ])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }

            if (fieldInfo.FieldType is PointerTypeSyntax)
            {
                // public static explicit operator MSIHANDLE(IntPtr value) => new MSIHANDLE(value.ToPointer());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                        name,
                        [Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer))))))])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));

                // public static explicit operator MSIHANDLE(UIntPtr value) => new MSIHANDLE(value.ToPointer());
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                    .AddParameterListParameters(Parameter(UIntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                        name,
                        [Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer))))))])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }
        }
        else if (fieldInfo.FieldType is PointerTypeSyntax pts && IsVoid(pts.ElementType))
        {
            // void* typedefs should still get IntPtr conversions for convenience like their HANDLE counterparts.

            // public static implicit operator IntPtr(PSID value) => new IntPtr(value.Value);
            ExpressionSyntax valueValueArg = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldIdentifierName);
            members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), IntPtrTypeSyntax)
                .AddParameterListParameters(Parameter(name.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                .WithExpressionBody(ArrowExpressionClause(
                    ObjectCreationExpression(IntPtrTypeSyntax, [Argument(valueValueArg)])))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                .WithSemicolonToken(SemicolonWithLineFeed));

            // public static explicit operator PSID(IntPtr value) => new PSID(value.ToPointer());
            members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                .AddParameterListParameters(Parameter(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                    name,
                    [Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer))))))])))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                .WithSemicolonToken(SemicolonWithLineFeed));

            // public static explicit operator PSID(UIntPtr value) => new PSID(value.ToPointer());
            members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ExplicitKeyword), name)
                .AddParameterListParameters(Parameter(UIntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(
                    name,
                    [Argument(CastExpression(fieldInfo.FieldType, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, IdentifierName(nameof(IntPtr.ToPointer))))))])))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                .WithSemicolonToken(SemicolonWithLineFeed));
        }

        foreach (string alsoUsableForValue in this.GetAlsoUsableForValues(typeDef))
        {
            // Add implicit conversion operators for each AlsoUsableFor attribute on the struct.
            var fullyQualifiedAlsoUsableForValue = $"{this.Reader.GetString(typeDef.Namespace)}.{alsoUsableForValue}";
            if (this.TryGenerateType(fullyQualifiedAlsoUsableForValue))
            {
                IdentifierNameSyntax alsoUsableForTypeSymbolName = IdentifierName(alsoUsableForValue);
                ExpressionSyntax valueValueArg = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldIdentifierName);

                // public static implicit operator HINSTANCE(HMODULE value) => new HINSTANCE(value.Value);
                members = members.Add(ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), alsoUsableForTypeSymbolName)
                    .AddParameterListParameters(Parameter(name.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
                    .WithExpressionBody(ArrowExpressionClause(
                        ObjectCreationExpression(alsoUsableForTypeSymbolName, [Argument(valueValueArg)])))
                    .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
                    .WithSemicolonToken(SemicolonWithLineFeed));
            }
        }

        switch (name.Identifier.ValueText)
        {
            case "PWSTR":
            case "PSTR":
                members = AddOrReplaceMembers(members, this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                this.TryGenerateType("Windows.Win32.Foundation.PC" + name.Identifier.ValueText.Substring(1)); // the template references its constant version
                break;
            case "VARIANT_BOOL":
                members = AddOrReplaceMembers(members, this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                this.TryGenerateConstant("VARIANT_TRUE", out _); // the template references its constant version
                this.TryGenerateConstant("VARIANT_FALSE", out _); // the template references its constant version
                break;
            case "BSTR":
            case "HRESULT":
            case "NTSTATUS":
            case "BOOL":
            case "BOOLEAN":
                members = AddOrReplaceMembers(members, this.ExtractMembersFromTemplate(name.Identifier.ValueText));
                break;
            default:
                break;
        }

        SyntaxList<MemberDeclarationSyntax> AddOrReplaceMembers(SyntaxList<MemberDeclarationSyntax> members, IEnumerable<MemberDeclarationSyntax> adds)
        {
            foreach (MemberDeclarationSyntax add in adds)
            {
                if (add is BaseMethodDeclarationSyntax addedMethod && members.FirstOrDefault(m => m is BaseMethodDeclarationSyntax existingMethod && IsSignatureMatch(addedMethod, existingMethod)) is MethodDeclarationSyntax replacedMethod)
                {
                    members = members.Replace(replacedMethod, addedMethod);
                }
                else
                {
                    members = members.Add(add);
                }
            }

            return members;
        }

        SyntaxTokenList structModifiers = [TokenWithSpace(this.Visibility)];
        if (RequiresUnsafe(fieldInfo.FieldType))
        {
            structModifiers = structModifiers.Add(TokenWithSpace(SyntaxKind.UnsafeKeyword));
        }

        string debuggerDisplay;
        if (members.Where(x => x is PropertyDeclarationSyntax && ((PropertyDeclarationSyntax)x).Identifier.ValueText == "DebuggerDisplay").Any())
        {
            debuggerDisplay = "{DebuggerDisplay,nq}";
        }
        else
        {
            debuggerDisplay = "{" + fieldName + "}";
        }

        AttributeSyntax[] attributes = [DebuggerDisplay(debuggerDisplay)];

        // If we're using source generators, generate a NativeMarshaller attribute pointing to the custom marshaller.
        if (this.UseSourceGenerators)
        {
            string ns = this.Reader.GetString(typeDef.Namespace);
            string typedefMarshaller = this.RequestCustomTypeDefMarshaller($"{ns}.{name.Identifier.ValueText}", fieldType.Type);
            AttributeSyntax nativeMarshallingAttribute = Attribute(IdentifierName("NativeMarshalling"))
                .AddArgumentListArguments(
                AttributeArgument(TypeOfExpression(IdentifierName(typedefMarshaller))));
            attributes = [.. attributes, nativeMarshallingAttribute];
        }

        structModifiers = structModifiers.Add(TokenWithSpace(SyntaxKind.ReadOnlyKeyword)).Add(TokenWithSpace(SyntaxKind.PartialKeyword));
        StructDeclarationSyntax result = StructDeclaration(name.Identifier)
            .WithBaseList(BaseList(SimpleBaseType(GenericName(nameof(IEquatable<>), TypeArgumentList(name).WithGreaterThanToken(TokenWithLineFeed(SyntaxKind.GreaterThanToken))))).WithColonToken(TokenWithSpace(SyntaxKind.ColonToken)))
            .WithMembers(members)
            .WithModifiers(structModifiers)
            .AddAttributeLists(AttributeList([.. attributes]).WithCloseBracketToken(TokenWithLineFeed(SyntaxKind.CloseBracketToken)));

        result = this.AddApiDocumentation(name.Identifier.ValueText, result);
        return result;
    }

    private IEnumerable<MemberDeclarationSyntax> CreateCommonTypeDefMembers(IdentifierNameSyntax structName, TypeSyntax fieldType, IdentifierNameSyntax fieldName)
    {
        // Add constructor
        IdentifierNameSyntax valueParameter = IdentifierName("value");
        MemberAccessExpressionSyntax fieldAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), fieldName);
        yield return ConstructorDeclaration(structName.Identifier, [Parameter(fieldType.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier)])
            .AddModifiers(TokenWithSpace(this.Visibility))
            .WithExpressionBody(ArrowExpressionClause(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, fieldAccessExpression, valueParameter).WithOperatorToken(TokenWithSpaces(SyntaxKind.EqualsToken))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // Add an IntPtr constructor as well if the field type is a pointer.
        if (fieldType is PointerTypeSyntax)
        {
            // public HWND(IntPtr value) : this((void*)value) { }
            yield return ConstructorDeclaration(structName.Identifier, [Parameter(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier)])
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithInitializer(ConstructorInitializer(SyntaxKind.ThisConstructorInitializer, [Argument(UncheckedExpression(CastExpression(fieldType, valueParameter)))]))
                .WithBody(Block());
        }

        // If this typedef struct represents a pointer, add an IsNull property.
        if (fieldType is IdentifierNameSyntax { Identifier: { Value: nameof(IntPtr) or nameof(UIntPtr) } }
            or PointerTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword } })
        {
            // internal static HWND Null => default;
            yield return PropertyDeclaration(structName.WithTrailingTrivia(TriviaList(Space)), "Null")
                .WithExpressionBody(ArrowExpressionClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression)))
                .AddModifiers(TokenWithSpace(this.Visibility), TokenWithSpace(SyntaxKind.StaticKeyword))
                .WithSemicolonToken(SemicolonWithLineFeed);

            // internal static bool IsNull => value == default;
            yield return PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), "IsNull")
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithExpressionBody(ArrowExpressionClause(BinaryExpression(SyntaxKind.EqualsExpression, fieldName, LiteralExpression(SyntaxKind.DefaultLiteralExpression))))
                .WithSemicolonToken(SemicolonWithLineFeed);
        }

        // public static implicit operator int(HWND value) => value.Value;
        yield return ConversionOperatorDeclaration(Token(SyntaxKind.ImplicitKeyword), fieldType)
            .AddParameterListParameters(Parameter(structName.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
            .WithExpressionBody(ArrowExpressionClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, valueParameter, fieldName)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static explicit operator HWND(int value) => new HWND(value);
        // Except make converting char* or byte* to typedefs representing strings, and LPARAM/WPARAM to nint/nuint, implicit.
        SyntaxKind explicitOrImplicitModifier = ImplicitConversionTypeDefs.Contains(structName.Identifier.ValueText) ? SyntaxKind.ImplicitKeyword : SyntaxKind.ExplicitKeyword;
        yield return ConversionOperatorDeclaration(Token(explicitOrImplicitModifier), structName)
            .AddParameterListParameters(Parameter(fieldType.WithTrailingTrivia(TriviaList(Space)), valueParameter.Identifier))
            .WithExpressionBody(ArrowExpressionClause(ObjectCreationExpression(structName, [Argument(valueParameter)])))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword)) // operators MUST be public
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static bool operator ==(HANDLE left, HANDLE right) => left.Value == right.Value;
        IdentifierNameSyntax? leftParameter = IdentifierName("left");
        IdentifierNameSyntax? rightParameter = IdentifierName("right");
        yield return OperatorDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), TokenWithNoSpace(SyntaxKind.EqualsEqualsToken))
            .WithOperatorKeyword(TokenWithSpace(SyntaxKind.OperatorKeyword))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(
                Parameter(structName.WithTrailingTrivia(TriviaList(Space)), leftParameter.Identifier),
                Parameter(structName.WithTrailingTrivia(TriviaList(Space)), rightParameter.Identifier))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, leftParameter, fieldName),
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, rightParameter, fieldName))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public static bool operator !=(HANDLE left, HANDLE right) => !(left == right);
        yield return OperatorDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Token(SyntaxKind.ExclamationEqualsToken))
            .WithOperatorKeyword(TokenWithSpace(SyntaxKind.OperatorKeyword))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(
                Parameter(structName.WithTrailingTrivia(TriviaList(Space)), leftParameter.Identifier),
                Parameter(structName.WithTrailingTrivia(TriviaList(Space)), rightParameter.Identifier))
            .WithExpressionBody(ArrowExpressionClause(
                PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    ParenthesizedExpression(BinaryExpression(SyntaxKind.EqualsExpression, leftParameter, rightParameter)))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public bool Equals(HWND other) => this.Value == other.Value;
        IdentifierNameSyntax other = IdentifierName("other");
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier(nameof(IEquatable<>.Equals)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(Parameter(structName.WithTrailingTrivia(TriviaList(Space)), other.Identifier))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    fieldAccessExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, other, fieldName))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public override bool Equals(object obj) => obj is HWND other && this.Equals(other);
        IdentifierNameSyntax objParam = IdentifierName("obj");
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier(nameof(IEquatable<>.Equals)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
            .AddParameterListParameters(Parameter(PredefinedType(TokenWithSpace(SyntaxKind.ObjectKeyword)), objParam.Identifier))
            .WithExpressionBody(ArrowExpressionClause(
                BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    IsPatternExpression(objParam, DeclarationPattern(structName, SingleVariableDesignation(Identifier("other")))),
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(nameof(Equals))),
                        [Argument(IdentifierName("other"))]))))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public override int GetHashCode() => unchecked((int)this.Value); // if Value is a pointer
        // public override int GetHashCode() => this.Value.GetHashCode(); // if Value is not a pointer
        ExpressionSyntax hashExpr = fieldType is PointerTypeSyntax ?
            UncheckedExpression(CastExpression(PredefinedType(TokenWithNoSpace(SyntaxKind.IntKeyword)), fieldAccessExpression)) :
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, fieldAccessExpression, IdentifierName(nameof(object.GetHashCode))));
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.IntKeyword)), Identifier(nameof(object.GetHashCode)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
            .WithExpressionBody(ArrowExpressionClause(hashExpr))
            .WithSemicolonToken(SemicolonWithLineFeed);

        // public override string ToString() => $"0x{this.Value:x}";
        string valueAsNumber = fieldType is PointerTypeSyntax ? "(nuint)this.Value" : "this.Value";
        yield return MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.StringKeyword)), Identifier(nameof(object.ToString)))
            .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
            .WithExpressionBody(ArrowExpressionClause(SyntaxFactory.ParseExpression("$\"0x{" + valueAsNumber + ":x}\"")))
            .WithSemicolonToken(SemicolonWithLineFeed);
    }

    private List<string> GetAlsoUsableForValues(TypeDefinition typeDef)
    {
        List<string> alsoUsableForValues = new();
        foreach (CustomAttributeHandle ah in typeDef.GetCustomAttributes())
        {
            CustomAttribute a = this.Reader.GetCustomAttribute(ah);
            if (MetadataUtilities.IsAttribute(this.Reader, a, InteropDecorationNamespace, AlsoUsableForAttribute))
            {
                CustomAttributeValue<TypeSyntax> attributeData = a.DecodeValue(CustomAttributeTypeProvider.Instance);
                string alsoUsableForValue = (string)(attributeData.FixedArguments[0].Value ?? throw new GenerationFailedException("Missing AlsoUsableFor attribute."));
                alsoUsableForValues.Add(alsoUsableForValue);
            }
        }

        return alsoUsableForValues;
    }

    private bool IsSafeHandleCompatibleTypeDef(TypeHandleInfo? typeDef)
    {
        return this.TryGetTypeDefFieldType(typeDef, out TypeHandleInfo? fieldType) && this.IsSafeHandleCompatibleTypeDefFieldType(fieldType);
    }

    private bool IsSafeHandleCompatibleTypeDefFieldType(TypeHandleInfo? fieldType)
    {
        return fieldType is PointerTypeHandleInfo
            || fieldType is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr };
    }
}
