// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Media.DirectShow;
using Windows.Win32.Security.Cryptography;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.System.Console;
using Windows.Win32.UI.Shell;
using VARDESC = Windows.Win32.System.Com.VARDESC;

[Trait("WindowsOnly", "true")]
public class BasicTests
{
    private const int FILE_FLAG_DELETE_ON_CLOSE = 0x04000000; // remove when https://github.com/microsoft/win32metadata/issues/98 is fixed.
    private readonly ITestOutputHelper logger;

    public BasicTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    internal delegate uint GetTickCountDelegate();

    public static object[][] InterestingDecimalValue => new object[][]
    {
        new object[] { 0.0m },
        new object[] { 1.2m },
        new object[] { -1.2m },
        new object[] { decimal.MinValue },
        new object[] { decimal.MaxValue },
    };

    [Fact]
    public unsafe void AlsoUsableForImplicitConversion()
    {
        BCRYPT_KEY_HANDLE bcryptKeyHandle = new((void*)5);
        BCRYPT_HANDLE bcryptHandle = bcryptKeyHandle;
        Assert.Equal(bcryptKeyHandle, bcryptHandle);
    }

    [Fact]
    public void GetTickCount_Nonzero()
    {
        uint result = PInvoke.GetTickCount();
        Assert.NotEqual(0u, result);
    }

    [Fact]
    public void DISPLAYCONFIG_VIDEO_SIGNAL_INFO_Test()
    {
        DISPLAYCONFIG_VIDEO_SIGNAL_INFO i = default;
        i.pixelRate = 5;

        // TODO: write code that sets/gets memory on the inner struct (e.g. videoStandard).
    }

    ////[Fact]
    ////public void E_PDB_LIMIT()
    ////{
    ////    // We are very particular about the namespace the generated type comes from to ensure it is as expected.
    ////    HRESULT hr = global::Microsoft.Dia.Constants.E_PDB_LIMIT;
    ////    Assert.Equal(-2140340211, hr.Value);
    ////}

    [Fact]
    public void BSTR_ToString()
    {
        BSTR bstr = (BSTR)Marshal.StringToBSTR("hi");
        try
        {
            Assert.Equal("hi", bstr.ToString());
        }
        finally
        {
            PInvoke.SysFreeString(bstr);
        }
    }

    [Fact]
    public void BSTR_ToString_Null()
    {
        BSTR bstr = default;
        Assert.Null(bstr.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("hello")]
    public void BSTR_Length(string value)
    {
        BSTR bstr = (BSTR)Marshal.StringToBSTR(value);
        try
        {
            Assert.Equal(value.Length, bstr.Length);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    [Fact]
    public void BSTR_Length_Null()
    {
        Assert.Equal(0, default(BSTR).Length);
    }

    [Fact]
    public unsafe void BSTR_ImplicitConversionTo_ReadOnlySpan()
    {
        BSTR bstr = (BSTR)Marshal.StringToBSTR("hi");
        try
        {
            ReadOnlySpan<char> span = bstr;
            Assert.Equal(2, span.Length);
            Assert.Equal('h', span[0]);
            Assert.Equal('i', span[1]);
        }
        finally
        {
            PInvoke.SysFreeString(bstr);
        }
    }

    [Fact]
    public unsafe void BSTR_AsSpan()
    {
        BSTR bstr = (BSTR)Marshal.StringToBSTR("hi");
        try
        {
            ReadOnlySpan<char> span = bstr.AsSpan();
            Assert.Equal(2, span.Length);
            Assert.Equal('h', span[0]);
            Assert.Equal('i', span[1]);
        }
        finally
        {
            PInvoke.SysFreeString(bstr);
        }
    }

    [Theory]
    [MemberData(nameof(InterestingDecimalValue))]
    public void DecimalConversion(decimal value)
    {
        DECIMAL nativeDecimal = new(value);
        decimal valueRoundTripped = nativeDecimal;
        Assert.Equal(value, valueRoundTripped);

#if NET5_0_OR_GREATER
        nativeDecimal = value;
        valueRoundTripped = nativeDecimal;
        Assert.Equal(value, valueRoundTripped);
#endif
    }

    [Fact]
    public void HandlesOverrideEquals()
    {
        HANDLE handle5 = new((IntPtr)5);
        HANDLE handle8 = new((IntPtr)8);

        Assert.True(handle5.Equals((object)handle5));
        Assert.False(handle5.Equals((object)handle8));
        Assert.False(handle5.Equals(null));
    }

    [Fact]
    public void HandlesOverride_GetHashCode()
    {
        HANDLE handle5 = new((IntPtr)5);
        HANDLE handle8 = new((IntPtr)8);

        Assert.NotEqual(handle5.GetHashCode(), handle8.GetHashCode());
    }

    [Fact]
    public void HandlesImplementsIEquatable()
    {
        var handle5 = new HANDLE((IntPtr)5);
        IEquatable<HANDLE> handle5Equatable = handle5;
        var handle8 = new HANDLE((IntPtr)8);
        Assert.True(handle5Equatable.Equals(handle5));
        Assert.False(handle5Equatable.Equals(handle8));
    }

    [Fact]
    public void HANDLE_OverridesEqualityOperator()
    {
        var handle5 = new HANDLE((IntPtr)5);
        var handle5b = handle5;
        var handle8 = new HANDLE((IntPtr)8);
        Assert.True(handle5 == handle5b);
        Assert.False(handle5 != handle5b);
        Assert.True(handle5 != handle8);
        Assert.False(handle5 == handle8);
    }

    [Fact]
    public void GetWindowText_FriendlyOverload()
    {
        HWND hwnd = PInvoke.GetForegroundWindow();
        Span<char> text = stackalloc char[100];
        int len = PInvoke.GetWindowText(hwnd, text);
        ////Assert.NotEqual(0, len); // This can fail on devdiv account test runs
        string title = text.Slice(0, len).ToString();
        this.logger.WriteLine(title);
    }

    [Fact]
    public void CreateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using var fileHandle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE,
            FILE_SHARE_MODE.FILE_SHARE_NONE,
            lpSecurityAttributes: default,
            FILE_CREATION_DISPOSITION.CREATE_NEW,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_TEMPORARY | (FILE_FLAGS_AND_ATTRIBUTES)FILE_FLAG_DELETE_ON_CLOSE,
            hTemplateFile: null);
        Assert.True(File.Exists(path));
        fileHandle.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HRESULT_Succeeded()
    {
        Assert.True(((HRESULT)0).Succeeded);
        Assert.True(((HRESULT)1).Succeeded);
        Assert.False(((HRESULT)(-1)).Succeeded);
    }

    [Fact]
    public void HRESULT_Failed()
    {
        Assert.False(((HRESULT)0).Failed);
        Assert.False(((HRESULT)1).Failed);
        Assert.True(((HRESULT)(-1)).Failed);
    }

    [Fact]
    public void NTSTATUS_Severity()
    {
        Assert.Equal(NTSTATUS.Severity.Success, ((NTSTATUS)0).SeverityCode);
        Assert.Equal(NTSTATUS.Severity.Success, ((NTSTATUS)0x3fffffff).SeverityCode);

        Assert.Equal(NTSTATUS.Severity.Informational, ((NTSTATUS)0x40000000).SeverityCode);
        Assert.Equal(NTSTATUS.Severity.Informational, ((NTSTATUS)0x7fffffff).SeverityCode);

        Assert.Equal(NTSTATUS.Severity.Warning, ((NTSTATUS)0x80000000).SeverityCode);
        Assert.Equal(NTSTATUS.Severity.Warning, ((NTSTATUS)0xbfffffff).SeverityCode);

        Assert.Equal(NTSTATUS.Severity.Error, ((NTSTATUS)0xc0000000).SeverityCode);
        Assert.Equal(NTSTATUS.Severity.Error, ((NTSTATUS)0xffffffff).SeverityCode);
    }

    [Fact]
    public void FixedLengthInlineArrayAccess()
    {
        MainAVIHeader header = default;

#if NETCOREAPP
        header.dwReserved.AsSpan()[1] = 3;
        Assert.Equal(3u, header.dwReserved.AsSpan()[1]);
        Assert.Equal(3u, header.dwReserved[1]);
        Assert.Equal(3u, header.dwReserved[1]);
#endif

        header.dwReserved[2] = 4;
        Assert.Equal(4u, header.dwReserved.ReadOnlyItemRef(2));
        Assert.Equal(4u, header.dwReserved[2]);
    }

    [Fact]
    public void GetAllWindowsInfo()
    {
        bool windowReturn = PInvoke.EnumWindows(
            (HWND handle, LPARAM customParam) =>
            {
                int bufferSize = PInvoke.GetWindowTextLength(handle) + 1;
                unsafe
                {
                    fixed (char* windowNameChars = new char[bufferSize])
                    {
                        if (PInvoke.GetWindowText(handle, windowNameChars, bufferSize) == 0)
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            if (errorCode != 0)
                            {
                                throw new Win32Exception(errorCode);
                            }

                            return true;
                        }

                        string windowName = new string(windowNameChars);
                        this.logger.WriteLine(windowName);
                    }

                    return true;
                }
            },
            (LPARAM)0);
    }

    [Fact]
    public void FixedCharArrayToString_Length()
    {
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO info = default;
        info.strServiceShortName[0] = 'H';
        info.strServiceShortName[1] = 'i';
        Assert.Equal("Hi", info.strServiceShortName.ToString(2));
        Assert.Equal("Hi\0\0", info.strServiceShortName.ToString(4));
    }

    [Fact]
    public void FixedCharArray_ToString()
    {
        __char_64 fixedCharArray = default;
        Assert.Equal(string.Empty, fixedCharArray.ToString());
        fixedCharArray[0] = 'H';
        Assert.Equal("H", fixedCharArray.ToString());
        fixedCharArray[1] = 'i';
        Assert.Equal("Hi", fixedCharArray.ToString());

        for (int i = 0; i < fixedCharArray.Length; i++)
        {
            fixedCharArray[i] = 'x';
        }

        Assert.Equal(new string('x', fixedCharArray.Length), fixedCharArray.ToString());
    }

    [Fact]
    public void FixedLengthArray_ToArray()
    {
        __char_64 fixedCharArray = default;
        fixedCharArray = "hi";
        char[] expected = new char[fixedCharArray.Length];
        expected[0] = fixedCharArray[0];
        expected[1] = fixedCharArray[1];
        char[] actual = fixedCharArray.ToArray();
        Assert.Equal<char>(expected, actual);

        actual = fixedCharArray.ToArray(3);
        Assert.Equal<char>(expected.Take(3), actual);
    }

    [Fact]
    public void FixedLengthArray_CopyTo()
    {
        __char_64 fixedCharArray = default;
        fixedCharArray = "hi";
        Span<char> span = new char[fixedCharArray.Length];
        fixedCharArray.CopyTo(span);
        Assert.Equal('h', span[0]);
        Assert.Equal('i', span[1]);
        Assert.Equal(0, span[2]);

        span.Clear();
        fixedCharArray.CopyTo(span, 1);
        Assert.Equal('h', span[0]);
        Assert.Equal(0, span[1]);
    }

    [Fact]
    public void FixedLengthArray_Equals()
    {
        __char_64 fixedCharArray = default;
        fixedCharArray = "hi";

        Assert.True(fixedCharArray.Equals("hi"));
        Assert.False(fixedCharArray.Equals("h"));
        Assert.False(fixedCharArray.Equals("d"));
        Assert.False(fixedCharArray.Equals("hid"));

        char[] buffer = new char[fixedCharArray.Length];
        Assert.False(fixedCharArray.Equals(buffer));
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 2)));

        buffer[0] = 'h';
        buffer[1] = 'i';
        Assert.True(fixedCharArray.Equals(buffer));
        Assert.True(fixedCharArray.Equals(buffer.AsSpan(0, 2)));
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 3)));

        Assert.True(fixedCharArray.Equals(fixedCharArray.ToArray()));

        // This should be false because the remainder of the fixed length array is non-default.
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 1)));
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 0)));
    }

    [Fact]
    public void FixedCharArraySetWithString()
    {
        __char_64 fixedCharArray = default;

        fixedCharArray = null;
        Assert.Equal(string.Empty, fixedCharArray.ToString());

        fixedCharArray = string.Empty;
        Assert.Equal(string.Empty, fixedCharArray.ToString());

        fixedCharArray = "hi there";
        Assert.Equal("hi there", fixedCharArray.ToString());

        string expected = new string('x', fixedCharArray.Length);
        fixedCharArray = expected;
        Assert.Equal(expected, fixedCharArray.ToString());

        Assert.Throws<ArgumentException>(() => fixedCharArray = new string('x', fixedCharArray.Length + 1));
    }

    [Fact]
    public unsafe void GetProcAddress_String()
    {
        using FreeLibrarySafeHandle moduleHandle = PInvoke.LoadLibrary("kernel32");
        FARPROC pGetTickCount = PInvoke.GetProcAddress(moduleHandle, "GetTickCount");
        GetTickCountDelegate getTickCount = pGetTickCount.CreateDelegate<GetTickCountDelegate>();
        uint ticks = getTickCount();
        Assert.NotEqual(0u, ticks);

        var getTickCountPtr = (delegate*<uint>)pGetTickCount.Value;
        ticks = getTickCountPtr();
        Assert.NotEqual(0u, ticks);
    }

    [Fact]
    public void StructCharFieldsMarshaledAsUtf16()
    {
        Assert.Equal(128 * sizeof(char), Marshal.SizeOf<__char_128>());
        Assert.Equal(sizeof(char), Marshal.SizeOf<KEY_EVENT_RECORD._uChar_e__Union>());
    }

    [Fact]
    public void CHAR_MarshaledAsAnsi()
    {
        Assert.Equal(1, Marshal.SizeOf<CHAR>());
    }

    [Fact]
    public void CocreatableClassesWithImplicitInterfaces()
    {
        ShellLink shellLink = new ShellLink();
        IPersistFile persistFile = (IPersistFile)shellLink;
        Assert.NotNull(persistFile);
    }

    [Fact]
    public void PathParseIconLocation_Friendly()
    {
        string sourceString = "hi there,3";
        Span<char> buffer = new char[PInvoke.MAX_PATH];
        sourceString.AsSpan().CopyTo(buffer);
        int result = PInvoke.PathParseIconLocation(ref buffer);
        Assert.Equal(3, result);
        Assert.Equal("hi there", buffer.ToString());
    }

    [Fact]
    public void ZeroIsInvalidSafeHandle()
    {
        // Verify that zero is default and considered invalid.
        FreeLibrarySafeHandle h = new();
        Assert.True(h.IsInvalid);
        Assert.Equal(IntPtr.Zero, h.DangerousGetHandle());

        // Verify that -1 is considered valid.
        h = new(new IntPtr(-1), ownsHandle: false);
        Assert.False(h.IsInvalid);
    }

    [Fact]
    public void ZeroOrMinusOneIsInvalidSafeHandle()
    {
        // Verify that zero or -1 is default and considered invalid.
        DestroyCursorSafeHandle h = new();
        Assert.True(h.IsInvalid);
        Assert.True(h.DangerousGetHandle().ToInt32() is 0 or -1);

        // Verify that 0 is considered invalid.
        h = new(IntPtr.Zero, ownsHandle: false);
        Assert.True(h.IsInvalid);

        // Verify that -1 is considered invalid.
        h = new(new IntPtr(-1), ownsHandle: false);
        Assert.True(h.IsInvalid);
    }

    /// <summary>
    /// Verifies that structs with explicit field placement where managed and unmanaged values overlap do not cause a TypeLoadException.
    /// </summary>
    /// <remarks>
    /// This demonstrates the problem tracked by <see href="https://github.com/microsoft/CsWin32/issues/292">this bug</see>.
    /// </remarks>
    [Fact]
    public void LoadTypeWithOverlappedRefAndValueTypes_VARDESC()
    {
        VARDESC d = new()
        {
            Anonymous =
            {
                oInst = 3,
            },
        };
    }

    [Fact]
    public void FieldWithAssociatedEnum()
    {
        SHDESCRIPTIONID s = default;
        s.dwDescriptionId = SHDID_ID.SHDID_FS_FILE;
        Assert.Equal(SHDID_ID.SHDID_FS_FILE, s.dwDescriptionId);
    }
}
