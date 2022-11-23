// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal static class SimpleSyntaxFactory
{
    internal static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "as",
        "base",
        "checked",
        "decimal",
        "event",
        "in",
        "is",
        "internal",
        "lock",
        "object",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "ref",
        "string",
        "virtual",
    };

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
        AttributeSyntax? structLayoutAttribute = Attribute(SyntaxRecycleBin.Common.IdentifierName.StructLayout);
        args.Add(AttributeArgument(MemberAccessExpression(
                 SyntaxKind.SimpleMemberAccessExpression,
                 SyntaxRecycleBin.Common.IdentifierName.LayoutKind,
                 SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(LayoutKind), layoutKind)!))));

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
            args.Add(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxRecycleBin.Common.IdentifierName.CharSet, SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(CharSet), charSet)!)))
                .WithNameEquals(NameEquals(SyntaxRecycleBin.Common.IdentifierName.CharSet)));
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

        AttributeSyntax attribute = Attribute(SyntaxRecycleBin.Common.IdentifierName.MethodImpl)
            .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxRecycleBin.Common.IdentifierName.MethodImplOptions, SyntaxRecycleBin.Common.IdentifierName.AggressiveInlining)));
        return attribute;
    }

    internal static AttributeSyntax GUID(Guid guid)
    {
        return Attribute(SyntaxRecycleBin.Common.IdentifierName.Guid).AddArgumentListArguments(
            AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(guid.ToString().ToUpperInvariant()))));
    }

    internal static AttributeSyntax InterfaceType(ComInterfaceType interfaceType)
    {
        return Attribute(SyntaxRecycleBin.Common.IdentifierName.InterfaceType).AddArgumentListArguments(
            AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxRecycleBin.Common.IdentifierName.ComInterfaceType,
                SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(ComInterfaceType), interfaceType)!))));
    }

    internal static AttributeSyntax DllImport(MethodImport import, string moduleName, string? entrypoint, CharSet charSet = CharSet.Ansi)
    {
        List<AttributeArgumentSyntax> args = new();
        AttributeSyntax? dllImportAttribute = Attribute(SyntaxRecycleBin.Common.IdentifierName.DllImport);
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
            args.Add(AttributeArgument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxRecycleBin.Common.IdentifierName.CharSet, SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(CharSet), charSet)!)))
                .WithNameEquals(NameEquals(SyntaxRecycleBin.Common.IdentifierName.CharSet)));
        }

        dllImportAttribute = dllImportAttribute.WithArgumentList(FixTrivia(AttributeArgumentList().AddArguments(args.ToArray())));
        return dllImportAttribute;
    }

    internal static AttributeSyntax UnmanagedFunctionPointer(CallingConvention callingConvention)
    {
        return Attribute(SyntaxRecycleBin.Common.IdentifierName.UnmanagedFunctionPointerAttribute)
            .AddArgumentListArguments(AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxRecycleBin.Common.IdentifierName.CallingConvention,
                SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(CallingConvention), callingConvention)!))));
    }

    internal static AttributeSyntax MarshalAs(UnmanagedType unmanagedType, UnmanagedType? arraySubType = null, string? marshalCookie = null, string? marshalType = null, ExpressionSyntax? sizeConst = null, ExpressionSyntax? sizeParamIndex = null)
    {
        AttributeSyntax? marshalAs =
            Attribute(SyntaxRecycleBin.Common.IdentifierName.MarshalAs)
                .AddArgumentListArguments(AttributeArgument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxRecycleBin.Common.IdentifierName.UnmanagedType,
                        SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(UnmanagedType), unmanagedType)!))));

        if (arraySubType.HasValue && arraySubType.Value != 0 && unmanagedType is UnmanagedType.ByValArray or UnmanagedType.LPArray or UnmanagedType.SafeArray)
        {
            marshalAs = marshalAs.AddArgumentListArguments(
                AttributeArgument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxRecycleBin.Common.IdentifierName.UnmanagedType,
                        SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(UnmanagedType), arraySubType.Value)!)))
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
        return Attribute(SyntaxRecycleBin.Common.IdentifierName.DebuggerBrowsable)
            .AddArgumentListArguments(
            AttributeArgument(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxRecycleBin.Common.IdentifierName.DebuggerBrowsableState,
                SyntaxRecycleBin.Common.IdentifierName.GetFromLimitedSet(Enum.GetName(typeof(DebuggerBrowsableState), state)!))));
    }

    internal static AttributeSyntax DebuggerDisplay(string format)
    {
        return Attribute(SyntaxRecycleBin.Common.IdentifierName.DebuggerDisplay)
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
            CallingConvention.StdCall => FunctionPointerUnmanagedCallingConvention(SyntaxRecycleBin.Common.IdentifierName.Stdcall.Identifier),
            CallingConvention.Winapi => FunctionPointerUnmanagedCallingConvention(SyntaxRecycleBin.Common.IdentifierName.Stdcall.Identifier), // Winapi isn't a valid string, and only .NET 5 supports runtime-determined calling conventions like Winapi does.
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

        return ObjectCreationExpression(SyntaxRecycleBin.Common.IdentifierName.Guid).AddArgumentListArguments(
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

    internal static SyntaxToken SafeIdentifier(string name) => SafeIdentifierName(name).Identifier;

    internal static IdentifierNameSyntax SafeIdentifierName(string name) => IdentifierName(CSharpKeywords.Contains(name) ? "@" + name : name);

    internal static bool RequiresUnsafe(TypeSyntax? typeSyntax) => typeSyntax is PointerTypeSyntax || typeSyntax is FunctionPointerTypeSyntax;

    internal static ExpressionSyntax ToHexExpressionSyntax(MetadataReader reader, ConstantHandle constantHandle, bool assignableToSignedInteger)
    {
        Constant constant = reader.GetConstant(constantHandle);
        BlobReader blobReader = reader.GetBlobReader(constant.Value);
        BlobReader blobReader2 = reader.GetBlobReader(constant.Value);
        BlobReader blobReader3 = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.SByte => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadSByte()), blobReader2.ReadSByte())), SyntaxKind.SByteKeyword),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadByte()), blobReader2.ReadByte())),
            ConstantTypeCode.Int16 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt16()), blobReader2.ReadInt16())), SyntaxKind.ShortKeyword),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt16()), blobReader2.ReadUInt16())),
            ConstantTypeCode.Int32 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt32()), blobReader2.ReadInt32())), SyntaxKind.IntKeyword),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt32()), blobReader2.ReadUInt32())),
            ConstantTypeCode.Int64 => UncheckedSignedWrapper(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadInt64()), blobReader2.ReadInt64())), SyntaxKind.LongKeyword),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(ToHex(blobReader.ReadUInt64()), blobReader2.ReadUInt64())),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        ExpressionSyntax UncheckedSignedWrapper(LiteralExpressionSyntax value, SyntaxKind signedType)
        {
            return assignableToSignedInteger && value.Token.Text.StartsWith("0xF", StringComparison.OrdinalIgnoreCase)
                ? UncheckedExpression(CastExpression(PredefinedType(Token(signedType)), value))
                : value;
        }
    }

    internal static ExpressionSyntax ToExpressionSyntax(MetadataReader reader, ConstantHandle constantHandle)
    {
        Constant constant = reader.GetConstant(constantHandle);
        BlobReader blobReader = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blobReader.ReadBoolean() ? LiteralExpression(SyntaxKind.TrueLiteralExpression) : LiteralExpression(SyntaxKind.FalseLiteralExpression),
            ConstantTypeCode.Char => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadChar())),
            ConstantTypeCode.SByte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadSByte())),
            ConstantTypeCode.Byte => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadByte())),
            ConstantTypeCode.Int16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt16())),
            ConstantTypeCode.UInt16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt16())),
            ConstantTypeCode.Int32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt32())),
            ConstantTypeCode.UInt32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt32())),
            ConstantTypeCode.Int64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadInt64())),
            ConstantTypeCode.UInt64 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadUInt64())),
            ConstantTypeCode.Single => FloatExpression(blobReader.ReadSingle()),
            ConstantTypeCode.Double => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(blobReader.ReadDouble())),
            ConstantTypeCode.String => blobReader.ReadConstant(constant.TypeCode) is string value ? LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value)) : LiteralExpression(SyntaxKind.NullLiteralExpression),
            ConstantTypeCode.NullReference => LiteralExpression(SyntaxKind.NullLiteralExpression),
            _ => throw new NotSupportedException("ConstantTypeCode not supported: " + constant.TypeCode),
        };

        static ExpressionSyntax FloatExpression(float value)
        {
            return
                float.IsPositiveInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), SyntaxRecycleBin.Common.IdentifierName.PositiveInfinity) :
                float.IsNegativeInfinity(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), SyntaxRecycleBin.Common.IdentifierName.NegativeInfinity) :
                float.IsNaN(value) ? MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, PredefinedType(Token(SyntaxKind.FloatKeyword)), SyntaxRecycleBin.Common.IdentifierName.NaN) :
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value));
        }
    }
}
