﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;
using static Microsoft.Windows.CsWin32.SimpleSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal TypeSyntax? RequestSafeHandle(string releaseMethod)
    {
        if (!this.options.UseSafeHandles)
        {
            return null;
        }

        try
        {
            if (this.volatileCode.TryGetSafeHandleForReleaseMethod(releaseMethod, out TypeSyntax? safeHandleType))
            {
                return safeHandleType;
            }

            if (BclInteropSafeHandles.TryGetValue(releaseMethod, out TypeSyntax? bclType))
            {
                return bclType;
            }

            string safeHandleClassName = $"{releaseMethod}SafeHandle";

            MethodDefinitionHandle? releaseMethodHandle = this.GetMethodByName(releaseMethod);
            if (!releaseMethodHandle.HasValue)
            {
                throw new GenerationFailedException("Unable to find release method named: " + releaseMethod);
            }

            MethodDefinition releaseMethodDef = this.Reader.GetMethodDefinition(releaseMethodHandle.Value);
            string releaseMethodModule = this.GetNormalizedModuleName(releaseMethodDef.GetImport());

            IdentifierNameSyntax? safeHandleTypeIdentifier = IdentifierName(safeHandleClassName);
            safeHandleType = safeHandleTypeIdentifier;

            MethodSignature<TypeHandleInfo> releaseMethodSignature = releaseMethodDef.DecodeSignature(SignatureHandleProvider.Instance, null);
            TypeHandleInfo releaseMethodParameterTypeHandleInfo = releaseMethodSignature.ParameterTypes[0];
            TypeSyntaxAndMarshaling releaseMethodParameterType = releaseMethodParameterTypeHandleInfo.ToTypeSyntax(this.externSignatureTypeSettings, default);

            // If the release method takes more than one parameter, we can't generate a SafeHandle for it.
            if (releaseMethodSignature.RequiredParameterCount != 1)
            {
                safeHandleType = null;
            }

            // If the handle type is *always* 64-bits, even in 32-bit processes, SafeHandle cannot represent it, since it's based on IntPtr.
            // We could theoretically do this for x64-specific compilations though if required.
            if (!this.TryGetTypeDefFieldType(releaseMethodParameterTypeHandleInfo, out TypeHandleInfo? typeDefStructFieldType))
            {
                safeHandleType = null;
            }

            if (!this.IsSafeHandleCompatibleTypeDefFieldType(typeDefStructFieldType))
            {
                safeHandleType = null;
            }

            this.volatileCode.AddSafeHandleNameForReleaseMethod(releaseMethod, safeHandleType);

            if (safeHandleType is null)
            {
                return safeHandleType;
            }

            if (this.FindTypeSymbolIfAlreadyAvailable($"{this.Namespace}.{safeHandleType}") is object)
            {
                return safeHandleType;
            }

            this.RequestExternMethod(releaseMethodHandle.Value);

            // Collect all the known invalid values for this handle.
            // If no invalid values are given (e.g. BSTR), we'll just assume 0 is invalid.
            HashSet<IntPtr> invalidHandleValues = this.GetInvalidHandleValues(((HandleTypeHandleInfo)releaseMethodParameterTypeHandleInfo).Handle);
            IntPtr preferredInvalidValue = GetPreferredInvalidHandleValue(invalidHandleValues);

            CustomAttributeHandleCollection? atts = this.GetReturnTypeCustomAttributes(releaseMethodDef);
            TypeSyntaxAndMarshaling releaseMethodReturnType = releaseMethodSignature.ReturnType.ToTypeSyntax(this.externSignatureTypeSettings, atts);

            this.TryGetRenamedMethod(releaseMethod, out string? renamedReleaseMethod);

            var members = new List<MemberDeclarationSyntax>();

            MemberAccessExpressionSyntax thisHandle = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("handle"));
            ExpressionSyntax intptrZero = DefaultExpression(IntPtrTypeSyntax);
            ExpressionSyntax invalidHandleIntPtr = IntPtrExpr(preferredInvalidValue);

            // private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
            IdentifierNameSyntax invalidValueFieldName = IdentifierName("INVALID_HANDLE_VALUE");
            members.Add(FieldDeclaration(VariableDeclaration(IntPtrTypeSyntax).AddVariables(
                VariableDeclarator(invalidValueFieldName.Identifier).WithInitializer(EqualsValueClause(invalidHandleIntPtr))))
                .AddModifiers(TokenWithSpace(SyntaxKind.PrivateKeyword), TokenWithSpace(SyntaxKind.StaticKeyword), TokenWithSpace(SyntaxKind.ReadOnlyKeyword)));

            // public SafeHandle() : base(INVALID_HANDLE_VALUE, true)
            members.Add(ConstructorDeclaration(safeHandleTypeIdentifier.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithInitializer(ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, ArgumentList().AddArguments(
                    Argument(invalidValueFieldName),
                    Argument(LiteralExpression(SyntaxKind.TrueLiteralExpression)))))
                .WithBody(Block()));

            // public SafeHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(INVALID_HANDLE_VALUE, ownsHandle) { this.SetHandle(preexistingHandle); }
            const string preexistingHandleName = "preexistingHandle";
            const string ownsHandleName = "ownsHandle";
            members.Add(ConstructorDeclaration(safeHandleTypeIdentifier.Identifier)
                .AddModifiers(TokenWithSpace(this.Visibility))
                .AddParameterListParameters(
                    Parameter(Identifier(preexistingHandleName)).WithType(IntPtrTypeSyntax.WithTrailingTrivia(TriviaList(Space))),
                    Parameter(Identifier(ownsHandleName)).WithType(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)))
                        .WithDefault(EqualsValueClause(LiteralExpression(SyntaxKind.TrueLiteralExpression))))
                .WithInitializer(ConstructorInitializer(SyntaxKind.BaseConstructorInitializer, ArgumentList().AddArguments(
                    Argument(invalidValueFieldName),
                    Argument(IdentifierName(ownsHandleName)))))
                .WithBody(Block().AddStatements(
                    ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("SetHandle")))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName(preexistingHandleName)))))))));

            // public override bool IsInvalid => this.handle.ToInt64() == 0 || this.handle.ToInt64() == -1;
            ExpressionSyntax thisHandleToInt64 = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, thisHandle, IdentifierName(nameof(IntPtr.ToInt64))), ArgumentList());
            ExpressionSyntax overallTest = invalidHandleValues.Count == 0
                ? LiteralExpression(SyntaxKind.FalseLiteralExpression)
                : CompoundExpression(SyntaxKind.LogicalOrExpression, invalidHandleValues.Select(v => BinaryExpression(SyntaxKind.EqualsExpression, thisHandleToInt64, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(v.ToInt64())))));
            members.Add(PropertyDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), nameof(SafeHandle.IsInvalid))
                .AddModifiers(TokenWithSpace(SyntaxKind.PublicKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword))
                .WithExpressionBody(ArrowExpressionClause(overallTest))
                .WithSemicolonToken(SemicolonWithLineFeed));

            // (struct)this.handle or (struct)checked((fieldType)(nint))this.handle, as appropriate.
            bool implicitConversion = typeDefStructFieldType is PrimitiveTypeHandleInfo { PrimitiveTypeCode: PrimitiveTypeCode.IntPtr } or PointerTypeHandleInfo;
            ArgumentSyntax releaseHandleArgument = Argument(CastExpression(
                releaseMethodParameterType.Type,
                implicitConversion ? thisHandle : CheckedExpression(CastExpression(typeDefStructFieldType!.ToTypeSyntax(this.fieldTypeSettings, null).Type, CastExpression(IdentifierName("nint"), thisHandle)))));

            // protected override bool ReleaseHandle() => ReleaseMethod((struct)this.handle);
            // Special case release functions based on their return type as follows: (https://github.com/microsoft/win32metadata/issues/25)
            //  * bool => true is success
            //  * int => zero is success
            //  * uint => zero is success
            //  * byte => non-zero is success
            ExpressionSyntax releaseInvocation = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(this.options.ClassName),
                    IdentifierName(renamedReleaseMethod ?? releaseMethod)),
                ArgumentList().AddArguments(releaseHandleArgument));
            BlockSyntax? releaseBlock = null;
            if (!(releaseMethodReturnType.Type is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.BoolKeyword } } ||
                releaseMethodReturnType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "BOOL" } } }))
            {
                switch (releaseMethodReturnType.Type)
                {
                    case PredefinedTypeSyntax predefined:
                        SyntaxKind returnType = predefined.Keyword.Kind();
                        if (returnType == SyntaxKind.IntKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.UIntKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.ByteKeyword)
                        {
                            releaseInvocation = BinaryExpression(SyntaxKind.NotEqualsExpression, releaseInvocation, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)));
                        }
                        else if (returnType == SyntaxKind.VoidKeyword)
                        {
                            releaseBlock = Block(
                                ExpressionStatement(releaseInvocation),
                                ReturnStatement(LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                        }
                        else
                        {
                            throw new NotSupportedException($"Return type {returnType} on release method {releaseMethod} not supported.");
                        }

                        break;
                    case QualifiedNameSyntax { Right: IdentifierNameSyntax identifierName }:
                        switch (identifierName.Identifier.ValueText)
                        {
                            case "NTSTATUS":
                                this.TryGenerateConstantOrThrow("STATUS_SUCCESS");
                                ExpressionSyntax statusSuccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.NTSTATUS"), IdentifierName("STATUS_SUCCESS"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, statusSuccess);
                                break;
                            case "HRESULT":
                                this.TryGenerateConstantOrThrow("S_OK");
                                ExpressionSyntax ok = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.HRESULT"), IdentifierName("S_OK"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, ok);
                                break;
                            case "WIN32_ERROR":
                                ExpressionSyntax noerror = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("winmdroot.Foundation.WIN32_ERROR"), IdentifierName("NO_ERROR"));
                                releaseInvocation = BinaryExpression(SyntaxKind.EqualsExpression, releaseInvocation, noerror);
                                break;
                            default:
                                throw new NotSupportedException($"Return type {identifierName.Identifier.ValueText} on release method {releaseMethod} not supported.");
                        }

                        break;
                }
            }

            MethodDeclarationSyntax releaseHandleDeclaration = MethodDeclaration(PredefinedType(TokenWithSpace(SyntaxKind.BoolKeyword)), Identifier("ReleaseHandle"))
                .AddModifiers(TokenWithSpace(SyntaxKind.ProtectedKeyword), TokenWithSpace(SyntaxKind.OverrideKeyword));
            releaseHandleDeclaration = releaseBlock is null
                ? releaseHandleDeclaration
                     .WithExpressionBody(ArrowExpressionClause(releaseInvocation))
                     .WithSemicolonToken(SemicolonWithLineFeed)
                : releaseHandleDeclaration
                    .WithBody(releaseBlock);
            members.Add(releaseHandleDeclaration);

            ClassDeclarationSyntax safeHandleDeclaration = ClassDeclaration(Identifier(safeHandleClassName))
                .AddModifiers(TokenWithSpace(this.Visibility))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(SafeHandleTypeSyntax))))
                .AddMembers(members.ToArray())
                .AddAttributeLists(AttributeList().AddAttributes(GeneratedCodeAttribute))
                .WithLeadingTrivia(ParseLeadingTrivia($@"
/// <summary>
/// Represents a Win32 handle that can be closed with <see cref=""{this.options.ClassName}.{renamedReleaseMethod ?? releaseMethod}""/>.
/// </summary>
"));

            this.volatileCode.AddSafeHandleType(safeHandleDeclaration);
            return safeHandleType;
        }
        catch (Exception ex)
        {
            throw new GenerationFailedException($"Failed while generating SafeHandle for {releaseMethod}.", ex);
        }
    }

    internal bool TryGetHandleReleaseMethod(EntityHandle handleStructDefHandle, [NotNullWhen(true)] out string? releaseMethod)
    {
        if (handleStructDefHandle.IsNil)
        {
            releaseMethod = null;
            return false;
        }

        if (handleStructDefHandle.Kind == HandleKind.TypeReference)
        {
            if (this.TryGetTypeDefHandle((TypeReferenceHandle)handleStructDefHandle, out TypeDefinitionHandle typeDefHandle))
            {
                return this.TryGetHandleReleaseMethod(typeDefHandle, out releaseMethod);
            }
        }
        else if (handleStructDefHandle.Kind == HandleKind.TypeDefinition)
        {
            return this.TryGetHandleReleaseMethod((TypeDefinitionHandle)handleStructDefHandle, out releaseMethod);
        }

        releaseMethod = null;
        return false;
    }

    internal bool TryGetHandleReleaseMethod(TypeDefinitionHandle handleStructDefHandle, [NotNullWhen(true)] out string? releaseMethod)
    {
        return this.MetadataIndex.HandleTypeReleaseMethod.TryGetValue(handleStructDefHandle, out releaseMethod);
    }

    private static IntPtr GetPreferredInvalidHandleValue(HashSet<IntPtr> invalidHandleValues) => invalidHandleValues.Contains(new IntPtr(-1)) ? new IntPtr(-1) : invalidHandleValues.FirstOrDefault();

    private bool IsHandle(EntityHandle typeDefOrRefHandle, out string? releaseMethodName)
    {
        switch (typeDefOrRefHandle.Kind)
        {
            case HandleKind.TypeReference when this.TryGetTypeDefHandle((TypeReferenceHandle)typeDefOrRefHandle, out TypeDefinitionHandle typeDefHandle):
                return this.IsHandle(typeDefHandle, out releaseMethodName);
            case HandleKind.TypeDefinition:
                return this.IsHandle((TypeDefinitionHandle)typeDefOrRefHandle, out releaseMethodName);
        }

        releaseMethodName = null;
        return false;
    }

    private bool IsHandle(TypeDefinitionHandle typeDefHandle, out string? releaseMethodName)
    {
        if (this.MetadataIndex.HandleTypeReleaseMethod.TryGetValue(typeDefHandle, out releaseMethodName))
        {
            return true;
        }

        // Special case handles that do not carry RAIIFree attributes.
        releaseMethodName = null;
        TypeDefinition typeDef = this.Reader.GetTypeDefinition(typeDefHandle);
        return this.Reader.StringComparer.Equals(typeDef.Name, "HGDIOBJ")
            || this.Reader.StringComparer.Equals(typeDef.Name, "HWND");
    }

    private HashSet<IntPtr> GetInvalidHandleValues(EntityHandle handle)
    {
        QualifiedTypeDefinitionHandle tdh;
        if (handle.Kind == HandleKind.TypeReference)
        {
            if (!this.TryGetTypeDefHandle((TypeReferenceHandle)handle, out tdh))
            {
                throw new GenerationFailedException("Unable to look up type definition.");
            }
        }
        else if (handle.Kind == HandleKind.TypeDefinition)
        {
            tdh = new QualifiedTypeDefinitionHandle(this, (TypeDefinitionHandle)handle);
        }
        else
        {
            throw new GenerationFailedException("Unexpected handle type.");
        }

        HashSet<IntPtr> invalidHandleValues = new();
        QualifiedTypeDefinition td = tdh.Resolve();
        foreach (CustomAttributeHandle ah in td.Definition.GetCustomAttributes())
        {
            CustomAttribute a = td.Reader.GetCustomAttribute(ah);
            if (MetadataUtilities.IsAttribute(td.Reader, a, InteropDecorationNamespace, InvalidHandleValueAttribute))
            {
                CustomAttributeValue<TypeSyntax> attributeData = a.DecodeValue(CustomAttributeTypeProvider.Instance);
                long invalidValue = (long)(attributeData.FixedArguments[0].Value ?? throw new GenerationFailedException("Missing invalid value attribute."));
                invalidHandleValues.Add((IntPtr)invalidValue);
            }
        }

        return invalidHandleValues;
    }
}
