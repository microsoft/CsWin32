// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// A cache of reusable syntax.
/// </summary>
internal class SyntaxRecycleBin
{
    private readonly Dictionary<string, IdentifierNameSyntax> identifierNameCache = new(StringComparer.Ordinal);
    private int total;
    private int unique;

    internal SyntaxToken Identifier(string name) => this.IdentifierName(name).Identifier;

    internal IdentifierNameSyntax IdentifierName(string name)
    {
        if (!this.identifierNameCache.TryGetValue(name, out IdentifierNameSyntax? result))
        {
            this.unique++;
            result = FastSyntaxFactory.IdentifierName(FastSyntaxFactory.Identifier(name).WithTrailingTrivia(Space));
            this.identifierNameCache.Add(name, result);
        }

        this.total++;
        return result;
    }

    internal static class Common
    {
        internal static class IdentifierName
        {
#pragma warning disable SA1307, SA1304, SA1311, SA1309, SA1310 // upper-case letters
            internal static readonly IdentifierNameSyntax AllowMultiple = FastSyntaxFactory.IdentifierName(nameof(AllowMultiple));
            internal static readonly IdentifierNameSyntax p0 = FastSyntaxFactory.IdentifierName(nameof(p0));
            internal static readonly IdentifierNameSyntax _0 = FastSyntaxFactory.IdentifierName(nameof(_0));
            internal static readonly IdentifierNameSyntax preexistingHandle = FastSyntaxFactory.IdentifierName(nameof(preexistingHandle));
            internal static readonly IdentifierNameSyntax ArgumentNullException = FastSyntaxFactory.IdentifierName(nameof(ArgumentNullException));
            internal static readonly IdentifierNameSyntax SequenceEqual = FastSyntaxFactory.IdentifierName(nameof(SequenceEqual));
            internal static new readonly IdentifierNameSyntax ToString = FastSyntaxFactory.IdentifierName(nameof(ToString));
            internal static readonly IdentifierNameSyntax AsReadOnlySpan = FastSyntaxFactory.IdentifierName(nameof(AsReadOnlySpan));
            internal static readonly IdentifierNameSyntax span = FastSyntaxFactory.IdentifierName(nameof(span));
            internal static readonly IdentifierNameSyntax CreateReadOnlySpan = FastSyntaxFactory.IdentifierName(nameof(CreateReadOnlySpan));
            internal static readonly IdentifierNameSyntax ToPointer = FastSyntaxFactory.IdentifierName(nameof(ToPointer));
            internal static readonly IdentifierNameSyntax ToInt32 = FastSyntaxFactory.IdentifierName(nameof(ToInt32));
            internal static readonly IdentifierNameSyntax AsRef = FastSyntaxFactory.IdentifierName(nameof(AsRef));
            internal static readonly IdentifierNameSyntax ArgumentException = FastSyntaxFactory.IdentifierName(nameof(ArgumentException));
            internal static readonly IdentifierNameSyntax target = FastSyntaxFactory.IdentifierName(nameof(target));
            internal static readonly IdentifierNameSyntax ArgumentOutOfRangeException = FastSyntaxFactory.IdentifierName(nameof(ArgumentOutOfRangeException));
            internal static readonly IdentifierNameSyntax p = FastSyntaxFactory.IdentifierName(nameof(p));
            internal static readonly IdentifierNameSyntax pLastExclusive = FastSyntaxFactory.IdentifierName(nameof(pLastExclusive));
            internal static readonly IdentifierNameSyntax left = FastSyntaxFactory.IdentifierName(nameof(left));
            internal static readonly IdentifierNameSyntax right = FastSyntaxFactory.IdentifierName(nameof(right));
            internal static readonly IdentifierNameSyntax obj = FastSyntaxFactory.IdentifierName(nameof(obj));
            internal static readonly IdentifierNameSyntax i = FastSyntaxFactory.IdentifierName(nameof(i));
            internal static readonly IdentifierNameSyntax pCh = FastSyntaxFactory.IdentifierName(nameof(pCh));
            internal static readonly IdentifierNameSyntax Clear = FastSyntaxFactory.IdentifierName(nameof(Clear));
            internal static readonly IdentifierNameSyntax length = FastSyntaxFactory.IdentifierName(nameof(length));
            internal static readonly IdentifierNameSyntax result = FastSyntaxFactory.IdentifierName(nameof(result));
            internal static readonly IdentifierNameSyntax initLength = FastSyntaxFactory.IdentifierName(nameof(initLength));
            internal static readonly IdentifierNameSyntax SkipInit = FastSyntaxFactory.IdentifierName(nameof(SkipInit));
            internal static readonly IdentifierNameSyntax CopyTo = FastSyntaxFactory.IdentifierName(nameof(CopyTo));
            internal static readonly IdentifierNameSyntax SpanLength = FastSyntaxFactory.IdentifierName(nameof(SpanLength));
            internal static readonly IdentifierNameSyntax index = FastSyntaxFactory.IdentifierName(nameof(SpanLength));
            internal static readonly IdentifierNameSyntax ToInt64 = FastSyntaxFactory.IdentifierName(nameof(ToInt64));
            internal static readonly IdentifierNameSyntax INVALID_HANDLE_VALUE = FastSyntaxFactory.IdentifierName(nameof(INVALID_HANDLE_VALUE));
            internal static readonly IdentifierNameSyntax S_OK = FastSyntaxFactory.IdentifierName(nameof(S_OK));
            internal static readonly IdentifierNameSyntax NO_ERROR = FastSyntaxFactory.IdentifierName(nameof(NO_ERROR));
            internal static readonly IdentifierNameSyntax STATUS_SUCCESS = FastSyntaxFactory.IdentifierName(nameof(STATUS_SUCCESS));
            internal static readonly IdentifierNameSyntax handle = FastSyntaxFactory.IdentifierName(nameof(handle));
            internal static readonly IdentifierNameSyntax Attribute = FastSyntaxFactory.IdentifierName(nameof(Attribute));
            internal static readonly IdentifierNameSyntax SetHandle = FastSyntaxFactory.IdentifierName(nameof(SetHandle));
            internal static readonly IdentifierNameSyntax DangerousAddRef = FastSyntaxFactory.IdentifierName(nameof(DangerousAddRef));
            internal static readonly IdentifierNameSyntax DangerousRelease = FastSyntaxFactory.IdentifierName(nameof(DangerousRelease));
            internal static readonly IdentifierNameSyntax SafeHandle = FastSyntaxFactory.IdentifierName(nameof(SafeHandle));
            internal static readonly IdentifierNameSyntax DangerousGetHandle = FastSyntaxFactory.IdentifierName(nameof(DangerousGetHandle));
            internal static readonly IdentifierNameSyntax Inherited = FastSyntaxFactory.IdentifierName(nameof(Inherited));
            internal static readonly IdentifierNameSyntax AttributeUsageAttribute = FastSyntaxFactory.IdentifierName(nameof(AttributeUsageAttribute));
            internal static readonly IdentifierNameSyntax UnscopedRefAttribute = FastSyntaxFactory.IdentifierName(nameof(UnscopedRefAttribute));
            internal static readonly IdentifierNameSyntax AggressiveInlining = FastSyntaxFactory.IdentifierName(nameof(AggressiveInlining));
            internal static readonly IdentifierNameSyntax AsSpan = FastSyntaxFactory.IdentifierName(nameof(AsSpan));
            internal static readonly IdentifierNameSyntax CallingConvention = FastSyntaxFactory.IdentifierName(nameof(CallingConvention));
            internal static readonly IdentifierNameSyntax CharSet = FastSyntaxFactory.IdentifierName(nameof(CharSet));
            internal static readonly IdentifierNameSyntax ComInterfaceType = FastSyntaxFactory.IdentifierName(nameof(ComInterfaceType));
            internal static readonly IdentifierNameSyntax CreateSpan = FastSyntaxFactory.IdentifierName(nameof(CreateSpan));
            internal static readonly IdentifierNameSyntax CreateDelegate = FastSyntaxFactory.IdentifierName(nameof(CreateDelegate));
            internal static readonly IdentifierNameSyntax DebuggerBrowsable = FastSyntaxFactory.IdentifierName(nameof(DebuggerBrowsable));
            internal static readonly IdentifierNameSyntax DebuggerBrowsableState = FastSyntaxFactory.IdentifierName(nameof(DebuggerBrowsableState));
            internal static readonly IdentifierNameSyntax GetBytes = FastSyntaxFactory.IdentifierName(nameof(GetBytes));
            internal static readonly IdentifierNameSyntax DebuggerDisplay = FastSyntaxFactory.IdentifierName(nameof(DebuggerDisplay));
            internal static readonly IdentifierNameSyntax LastIndexOf = FastSyntaxFactory.IdentifierName(nameof(LastIndexOf));
            internal static readonly IdentifierNameSyntax ownsHandle = FastSyntaxFactory.IdentifierName(nameof(ownsHandle));
            internal static readonly IdentifierNameSyntax Delegate = FastSyntaxFactory.IdentifierName(nameof(Delegate));
            internal static readonly IdentifierNameSyntax DllImport = FastSyntaxFactory.IdentifierName(nameof(DllImport));
            internal static new readonly IdentifierNameSyntax Equals = FastSyntaxFactory.IdentifierName(nameof(Equals));
            internal static new readonly IdentifierNameSyntax GetHashCode = FastSyntaxFactory.IdentifierName(nameof(GetHashCode));
            internal static readonly IdentifierNameSyntax fmtid = FastSyntaxFactory.IdentifierName(nameof(fmtid));
            internal static readonly IdentifierNameSyntax GetDelegateForFunctionPointer = FastSyntaxFactory.IdentifierName(nameof(GetDelegateForFunctionPointer));
            internal static readonly IdentifierNameSyntax Guid = FastSyntaxFactory.IdentifierName(nameof(Guid));
            internal static readonly IdentifierNameSyntax IndexOf = FastSyntaxFactory.IdentifierName(nameof(IndexOf));
            internal static readonly IdentifierNameSyntax HasValue = FastSyntaxFactory.IdentifierName(nameof(HasValue));
            internal static readonly IdentifierNameSyntax @this = FastSyntaxFactory.IdentifierName("@this");
            internal static readonly IdentifierNameSyntax InterfaceType = FastSyntaxFactory.IdentifierName(nameof(InterfaceType));
            internal static readonly IdentifierNameSyntax IntPtr = FastSyntaxFactory.IdentifierName(nameof(IntPtr));
            internal static readonly IdentifierNameSyntax LayoutKind = FastSyntaxFactory.IdentifierName(nameof(LayoutKind));
            internal static readonly IdentifierNameSyntax ToArray = FastSyntaxFactory.IdentifierName(nameof(ToArray));
            internal static readonly IdentifierNameSyntax Length = FastSyntaxFactory.IdentifierName(nameof(Length));
            internal static readonly IdentifierNameSyntax Marshal = FastSyntaxFactory.IdentifierName(nameof(Marshal));
            internal static readonly IdentifierNameSyntax MemoryMarshal = FastSyntaxFactory.IdentifierName(nameof(MemoryMarshal));
            internal static readonly IdentifierNameSyntax MarshalAs = FastSyntaxFactory.IdentifierName(nameof(MarshalAs));
            internal static readonly IdentifierNameSyntax MethodImpl = FastSyntaxFactory.IdentifierName(nameof(MethodImpl));
            internal static readonly IdentifierNameSyntax MethodImplOptions = FastSyntaxFactory.IdentifierName(nameof(MethodImplOptions));
            internal static readonly IdentifierNameSyntax ReadOnlyItemRef = FastSyntaxFactory.IdentifierName(nameof(ReadOnlyItemRef));
            internal static readonly IdentifierNameSyntax other = FastSyntaxFactory.IdentifierName(nameof(other));
            internal static readonly IdentifierNameSyntax nameof = FastSyntaxFactory.IdentifierName(nameof(nameof));
            internal static readonly IdentifierNameSyntax NaN = FastSyntaxFactory.IdentifierName(nameof(NaN));
            internal static readonly IdentifierNameSyntax NegativeInfinity = FastSyntaxFactory.IdentifierName(nameof(NegativeInfinity));
            internal static readonly IdentifierNameSyntax nint = FastSyntaxFactory.IdentifierName("nint");
            internal static readonly IdentifierNameSyntax nuint = FastSyntaxFactory.IdentifierName("nuint");
            internal static readonly IdentifierNameSyntax pid = FastSyntaxFactory.IdentifierName(nameof(pid));
            internal static readonly IdentifierNameSyntax PositiveInfinity = FastSyntaxFactory.IdentifierName(nameof(PositiveInfinity));
            internal static readonly IdentifierNameSyntax ReleaseHandle = FastSyntaxFactory.IdentifierName(nameof(ReleaseHandle));
            internal static readonly IdentifierNameSyntax Slice = FastSyntaxFactory.IdentifierName(nameof(Slice));
            internal static readonly IdentifierNameSyntax Stdcall = FastSyntaxFactory.IdentifierName(nameof(Stdcall));
            internal static readonly IdentifierNameSyntax StructLayout = FastSyntaxFactory.IdentifierName(nameof(StructLayout));
            internal static readonly IdentifierNameSyntax TDelegate = FastSyntaxFactory.IdentifierName(nameof(TDelegate));
            internal static readonly IdentifierNameSyntax UIntPtr = FastSyntaxFactory.IdentifierName(nameof(UIntPtr));
            internal static readonly IdentifierNameSyntax UnmanagedFunctionPointerAttribute = FastSyntaxFactory.IdentifierName(nameof(UnmanagedFunctionPointerAttribute));
            internal static readonly IdentifierNameSyntax Unsafe = FastSyntaxFactory.IdentifierName(nameof(Unsafe));
            internal static readonly IdentifierNameSyntax AttributeTargets = FastSyntaxFactory.IdentifierName(nameof(AttributeTargets));
            internal static readonly IdentifierNameSyntax UnmanagedType = FastSyntaxFactory.IdentifierName(nameof(UnmanagedType));
            internal static readonly IdentifierNameSyntax Method = FastSyntaxFactory.IdentifierName(nameof(Method));
            internal static readonly IdentifierNameSyntax Property = FastSyntaxFactory.IdentifierName(nameof(Property));
            internal static readonly IdentifierNameSyntax Parameter = FastSyntaxFactory.IdentifierName(nameof(Parameter));
            internal static readonly IdentifierNameSyntax GetReference = FastSyntaxFactory.IdentifierName(nameof(GetReference));
            internal static readonly IdentifierNameSyntax Value = FastSyntaxFactory.IdentifierName(nameof(Value));
            internal static readonly IdentifierNameSyntax lpVtbl = FastSyntaxFactory.IdentifierName(nameof(lpVtbl));
            internal static readonly IdentifierNameSyntax pThis = FastSyntaxFactory.IdentifierName(nameof(pThis));
            internal static readonly IdentifierNameSyntax value = FastSyntaxFactory.IdentifierName(nameof(value));
            internal static readonly IdentifierNameSyntax __result = FastSyntaxFactory.IdentifierName(nameof(__result));
            internal static readonly IdentifierNameSyntax data = FastSyntaxFactory.IdentifierName(nameof(data));
            internal static readonly IdentifierNameSyntax __retVal = FastSyntaxFactory.IdentifierName(nameof(__retVal));
            internal static readonly IdentifierNameSyntax IID_Guid = FastSyntaxFactory.IdentifierName(nameof(IID_Guid));
#pragma warning restore SA1307, SA1311, SA1304, SA1309, SA1310 // upper-case letters

            private static readonly SyntaxRecycleBin LimitedSet = new();

            internal static IdentifierNameSyntax GetFromLimitedSet(string name) => LimitedSet.IdentifierName(name);
        }

        internal static class PredefinedType
        {
            internal static readonly PredefinedTypeSyntax Char = PredefinedType(Token(SyntaxKind.CharKeyword));

            internal static readonly PredefinedTypeSyntax Boolean = PredefinedType(Token(SyntaxKind.BoolKeyword));

            internal static readonly PredefinedTypeSyntax SByte = PredefinedType(Token(SyntaxKind.SByteKeyword));

            internal static readonly PredefinedTypeSyntax Byte = PredefinedType(Token(SyntaxKind.ByteKeyword));

            internal static readonly PredefinedTypeSyntax Int16 = PredefinedType(Token(SyntaxKind.ShortKeyword));

            internal static readonly PredefinedTypeSyntax UInt16 = PredefinedType(Token(SyntaxKind.UShortKeyword));

            internal static readonly PredefinedTypeSyntax Int32 = PredefinedType(Token(SyntaxKind.IntKeyword));

            internal static readonly PredefinedTypeSyntax UInt32 = PredefinedType(Token(SyntaxKind.UIntKeyword));

            internal static readonly PredefinedTypeSyntax Int64 = PredefinedType(Token(SyntaxKind.LongKeyword));

            internal static readonly PredefinedTypeSyntax UInt64 = PredefinedType(Token(SyntaxKind.ULongKeyword));

            internal static readonly PredefinedTypeSyntax Single = PredefinedType(Token(SyntaxKind.FloatKeyword));

            internal static readonly PredefinedTypeSyntax Double = PredefinedType(Token(SyntaxKind.DoubleKeyword));

            internal static readonly PredefinedTypeSyntax Object = PredefinedType(Token(SyntaxKind.ObjectKeyword));

            internal static readonly PredefinedTypeSyntax String = PredefinedType(Token(SyntaxKind.StringKeyword));

            internal static readonly PredefinedTypeSyntax Void = PredefinedType(Token(SyntaxKind.VoidKeyword));
        }
    }
}
