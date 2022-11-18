// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

internal static class SimpleSyntaxFactory
{
    internal static readonly XmlTextSyntax DocCommentStart = XmlText(" ").WithLeadingTrivia(DocumentationCommentExterior("///"));
    internal static readonly XmlTextSyntax DocCommentEnd = XmlText(XmlTextNewLine("\n", continueXmlDocumentationComment: false));

    internal static readonly SyntaxToken SemicolonWithLineFeed = TokenWithLineFeed(SyntaxKind.SemicolonToken);
    internal static readonly IdentifierNameSyntax InlineArrayIndexerExtensionsClassName = IdentifierName("InlineArrayIndexerExtensions");
    internal static readonly TypeSyntax SafeHandleTypeSyntax = IdentifierName("SafeHandle");
    internal static readonly IdentifierNameSyntax IntPtrTypeSyntax = IdentifierName(nameof(IntPtr));
    internal static readonly IdentifierNameSyntax UIntPtrTypeSyntax = IdentifierName(nameof(UIntPtr));
    internal static readonly AttributeSyntax ComImportAttributeSyntax = Attribute(IdentifierName("ComImport"));
    internal static readonly AttributeSyntax PreserveSigAttributeSyntax = Attribute(IdentifierName("PreserveSig"));
    internal static readonly AttributeSyntax ObsoleteAttributeSyntax = Attribute(IdentifierName("Obsolete")).WithArgumentList(null);
    internal static readonly AttributeSyntax SupportedOSPlatformAttributeSyntax = Attribute(IdentifierName("SupportedOSPlatform"));
    internal static readonly AttributeSyntax UnscopedRefAttributeSyntax = Attribute(ParseName("UnscopedRef")).WithArgumentList(null);
    internal static readonly IdentifierNameSyntax SliceAtNullMethodName = IdentifierName("SliceAtNull");
    internal static readonly IdentifierNameSyntax IComIIDGuidInterfaceName = IdentifierName("IComIID");
    internal static readonly IdentifierNameSyntax ComIIDGuidPropertyName = IdentifierName("Guid");
    internal static readonly AttributeSyntax FieldOffsetAttributeSyntax = Attribute(IdentifierName("FieldOffset"));

    [return: NotNullIfNotNull("marshalAs")]
    internal static AttributeSyntax? MarshalAs(MarshalAsAttribute? marshalAs, Generator.NativeArrayInfo? nativeArrayInfo)
    {
        if (marshalAs is null)
        {
            return null;
        }

        // TODO: fill in more properties to match the original
        return MarshalAs(
            marshalAs.Value,
            marshalAs.ArraySubType,
            marshalAs.MarshalCookie,
            marshalAs.MarshalType,
            nativeArrayInfo?.CountConst.HasValue is true ? LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(nativeArrayInfo.Value.CountConst.Value)) : null,
            nativeArrayInfo?.CountParamIndex.HasValue is true ? LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(nativeArrayInfo.Value.CountParamIndex.Value)) : null);
    }

    internal static TypeSyntax MakeSpanOfT(TypeSyntax typeArgument) => GenericName(nameof(Span<int>)).AddTypeArgumentListArguments(typeArgument);

    internal static TypeSyntax MakeReadOnlySpanOfT(TypeSyntax typeArgument) => GenericName(nameof(ReadOnlySpan<int>)).AddTypeArgumentListArguments(typeArgument);

    internal static ExpressionSyntax CompoundExpression(SyntaxKind @operator, params ExpressionSyntax[] elements) =>
        elements.Aggregate((left, right) => BinaryExpression(@operator, left, right));

    internal static ExpressionSyntax CompoundExpression(SyntaxKind @operator, IEnumerable<ExpressionSyntax> elements) =>
        elements.Aggregate((left, right) => BinaryExpression(@operator, left, right));

    internal static ExpressionSyntax CompoundExpression(SyntaxKind @operator, ExpressionSyntax memberOf, params string[] memberNames) =>
        CompoundExpression(@operator, memberNames.Select(n => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, memberOf, IdentifierName(n))));

    internal static AttributeSyntax FieldOffset(int offset) => FieldOffsetAttributeSyntax.AddArgumentListArguments(AttributeArgument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(offset))));

    internal static AttributeSyntax StructLayout(TypeAttributes typeAttributes, TypeLayout layout = default, CharSet charSet = CharSet.Ansi)
    {
        LayoutKind layoutKind = (typeAttributes & TypeAttributes.ExplicitLayout) == TypeAttributes.ExplicitLayout ? LayoutKind.Explicit : LayoutKind.Sequential;
        List<AttributeArgumentSyntax> args = new();
        AttributeSyntax? structLayoutAttribute = Attribute(IdentifierName("StructLayout"));
        args.Add(AttributeArgument(MemberAccessExpression(
                 SyntaxKind.SimpleMemberAccessExpression,
                 IdentifierName(nameof(LayoutKind)),
                 IdentifierName(Enum.GetName(typeof(LayoutKind), layoutKind)!))));

        if (layout.PackingSize > 0)
        {
            args.Add(AttributeArgument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(layout.PackingSize)))
                    .WithNameEquals(NameEquals(nameof(StructLayoutAttribute.Pack))));
        }

        if (layout.Size > 0)
        {
            args.Add(AttributeArgument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(layout.Size)))
                    .WithNameEquals(NameEquals(nameof(StructLayoutAttribute.Size))));
        }

        if (charSet != CharSet.Ansi)
        {
            args.Add(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(CharSet)), IdentifierName(Enum.GetName(typeof(CharSet), charSet)!)))
                .WithNameEquals(NameEquals(IdentifierName(nameof(StructLayoutAttribute.CharSet)))));
        }

        structLayoutAttribute = structLayoutAttribute.WithArgumentList(FixTrivia(AttributeArgumentList().AddArguments(args.ToArray())));
        return structLayoutAttribute;
    }

    internal static AttributeSyntax MethodImpl(MethodImplOptions options)
    {
        if (options != MethodImplOptions.AggressiveInlining)
        {
            throw new NotImplementedException();
        }

        AttributeSyntax attribute = Attribute(IdentifierName("MethodImpl"))
            .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(MethodImplOptions)), IdentifierName(nameof(MethodImplOptions.AggressiveInlining)))));
        return attribute;
    }

    internal static AttributeSyntax GUID(Guid guid)
    {
        return Attribute(IdentifierName("Guid")).AddArgumentListArguments(
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(guid.ToString().ToUpperInvariant()))));
    }

    internal static AttributeSyntax InterfaceType(ComInterfaceType interfaceType)
    {
        return Attribute(IdentifierName("InterfaceType")).AddArgumentListArguments(
            AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(nameof(ComInterfaceType)),
                IdentifierName(Enum.GetName(typeof(ComInterfaceType), interfaceType)!))));
    }

    internal static AttributeSyntax DllImport(MethodImport import, string moduleName, string? entrypoint, CharSet charSet = CharSet.Ansi)
    {
        List<AttributeArgumentSyntax> args = new();
        AttributeSyntax? dllImportAttribute = Attribute(IdentifierName("DllImport"));
        args.Add(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(moduleName))));
        args.Add(AttributeArgument(LiteralExpression(SyntaxKind.TrueLiteralExpression)).WithNameEquals(NameEquals(nameof(DllImportAttribute.ExactSpelling))));

        if (entrypoint is not null)
        {
            args.Add(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(entrypoint)))
                    .WithNameEquals(NameEquals(nameof(DllImportAttribute.EntryPoint))));
        }

        if ((import.Attributes & MethodImportAttributes.SetLastError) == MethodImportAttributes.SetLastError)
        {
            args.Add(AttributeArgument(LiteralExpression(SyntaxKind.TrueLiteralExpression))
                    .WithNameEquals(NameEquals(nameof(DllImportAttribute.SetLastError))));
        }

        if (charSet != CharSet.Ansi)
        {
            args.Add(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(CharSet)), IdentifierName(Enum.GetName(typeof(CharSet), charSet)!)))
                .WithNameEquals(NameEquals(IdentifierName(nameof(DllImportAttribute.CharSet)))));
        }

        dllImportAttribute = dllImportAttribute.WithArgumentList(FixTrivia(AttributeArgumentList().AddArguments(args.ToArray())));
        return dllImportAttribute;
    }

    internal static AttributeSyntax UnmanagedFunctionPointer(CallingConvention callingConvention)
    {
        return Attribute(IdentifierName(nameof(UnmanagedFunctionPointerAttribute)))
            .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(nameof(CallingConvention)),
                IdentifierName(Enum.GetName(typeof(CallingConvention), callingConvention)!))));
    }

    internal static AttributeSyntax MarshalAs(UnmanagedType unmanagedType, UnmanagedType? arraySubType = null, string? marshalCookie = null, string? marshalType = null, ExpressionSyntax? sizeConst = null, ExpressionSyntax? sizeParamIndex = null)
    {
        AttributeSyntax? marshalAs =
            Attribute(IdentifierName("MarshalAs"))
                .AddArgumentListArguments(AttributeArgument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(nameof(UnmanagedType)),
                        IdentifierName(Enum.GetName(typeof(UnmanagedType), unmanagedType)!))));

        if (arraySubType.HasValue && arraySubType.Value != 0 && unmanagedType is UnmanagedType.ByValArray or UnmanagedType.LPArray or UnmanagedType.SafeArray)
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(nameof(UnmanagedType)),
                        IdentifierName(Enum.GetName(typeof(UnmanagedType), arraySubType.Value)!)))
                    .WithNameEquals(NameEquals(nameof(MarshalAsAttribute.ArraySubType))));
        }

        if (sizeConst is object)
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(sizeConst).WithNameEquals(NameEquals(nameof(MarshalAsAttribute.SizeConst))));
        }

        if (sizeParamIndex is object)
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(sizeParamIndex).WithNameEquals(NameEquals(nameof(MarshalAsAttribute.SizeParamIndex))));
        }

        if (!string.IsNullOrEmpty(marshalCookie))
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(marshalCookie!)))
                    .WithNameEquals(NameEquals(nameof(MarshalAsAttribute.MarshalCookie))));
        }

        if (!string.IsNullOrEmpty(marshalType))
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(marshalType!)))
                    .WithNameEquals(NameEquals(nameof(MarshalAsAttribute.MarshalType))));
        }

        return marshalAs;
    }

    internal static AttributeSyntax DebuggerBrowsable(DebuggerBrowsableState state)
    {
        return Attribute(IdentifierName("DebuggerBrowsable"))
            .AddArgumentListArguments(
            AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(nameof(DebuggerBrowsableState)),
                IdentifierName(Enum.GetName(typeof(DebuggerBrowsableState), state)!))));
    }

    internal static AttributeSyntax DebuggerDisplay(string format)
    {
        return Attribute(IdentifierName("DebuggerDisplay"))
            .AddArgumentListArguments(
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(format))));
    }

    internal static CrefParameterListSyntax ToCref(ParameterListSyntax parameterList) => CrefParameterList(FixTrivia(SeparatedList(parameterList.Parameters.Select(ToCref))));

    internal static CrefParameterSyntax ToCref(ParameterSyntax parameter)
        => CrefParameter(
            parameter.Modifiers.Any(SyntaxKind.InKeyword) ? TokenWithSpace(SyntaxKind.InKeyword) :
            parameter.Modifiers.Any(SyntaxKind.RefKeyword) ? TokenWithSpace(SyntaxKind.RefKeyword) :
            parameter.Modifiers.Any(SyntaxKind.OutKeyword) ? TokenWithSpace(SyntaxKind.OutKeyword) :
            default,
            parameter.Type!.WithoutTrailingTrivia());

    internal static FunctionPointerUnmanagedCallingConventionSyntax ToUnmanagedCallingConventionSyntax(CallingConvention callingConvention)
    {
        return callingConvention switch
        {
            CallingConvention.StdCall => FunctionPointerUnmanagedCallingConvention(Identifier("Stdcall")),
            CallingConvention.Winapi => FunctionPointerUnmanagedCallingConvention(Identifier("Stdcall")), // Winapi isn't a valid string, and only .NET 5 supports runtime-determined calling conventions like Winapi does.
            _ => throw new NotImplementedException(),
        };
    }

    internal static bool IsVoid(TypeSyntax typeSyntax) => typeSyntax is PredefinedTypeSyntax { Keyword: { RawKind: (int)SyntaxKind.VoidKeyword } };

    /// <summary>
    /// Creates the syntax for creating a new byte array populated with the specified data.
    /// e.g. <c>new byte[] { 0x01, 0x02 }</c>.
    /// </summary>
    /// <param name="bytes">The content of the array.</param>
    /// <returns>The array creation syntax.</returns>
    internal static ArrayCreationExpressionSyntax NewByteArray(ReadOnlySpan<byte> bytes)
    {
        ExpressionSyntax[] elements = new ExpressionSyntax[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            elements[i] = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(bytes[i]), bytes[i]));
        }

        return ArrayCreationExpression(
            ArrayType(PredefinedType(Token(SyntaxKind.ByteKeyword))).AddRankSpecifiers(ArrayRankSpecifier()),
            InitializerExpression(SyntaxKind.ArrayInitializerExpression, SeparatedList(elements)));
    }

    internal static unsafe string ToHex<T>(T value)
        where T : unmanaged
    {
        int fullHexLength = sizeof(T) * 2;
        string hex = string.Format(CultureInfo.InvariantCulture, "0x{0:X" + fullHexLength + "}", value);
        return hex;
    }

    internal static ObjectCreationExpressionSyntax GuidValue(CustomAttribute guidAttribute)
    {
        CustomAttributeValue<TypeSyntax> args = guidAttribute.DecodeValue(CustomAttributeTypeProvider.Instance);
        uint a = (uint)args.FixedArguments[0].Value!;
        ushort b = (ushort)args.FixedArguments[1].Value!;
        ushort c = (ushort)args.FixedArguments[2].Value!;
        byte d = (byte)args.FixedArguments[3].Value!;
        byte e = (byte)args.FixedArguments[4].Value!;
        byte f = (byte)args.FixedArguments[5].Value!;
        byte g = (byte)args.FixedArguments[6].Value!;
        byte h = (byte)args.FixedArguments[7].Value!;
        byte i = (byte)args.FixedArguments[8].Value!;
        byte j = (byte)args.FixedArguments[9].Value!;
        byte k = (byte)args.FixedArguments[10].Value!;

        return ObjectCreationExpression(IdentifierName(nameof(Guid))).AddArgumentListArguments(
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(a), a))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(b), b))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(c), c))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(d), d))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(e), e))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(f), f))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(g), g))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(h), h))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(i), i))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(j), j))),
            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(k), k))));
    }

    internal static ExpressionSyntax IntPtrExpr(IntPtr value) => ObjectCreationExpression(IntPtrTypeSyntax).AddArgumentListArguments(
        Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value.ToInt64()))));
}
