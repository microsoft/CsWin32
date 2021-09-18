// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectShow;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Console;
using Windows.Win32.System.ErrorReporting;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.DisplayDevices;
using Xunit;
using Xunit.Abstractions;

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
    public void Bool()
    {
        BOOL b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOL));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void NotLossyConversionBetweenBoolAndBOOL(int ordinal)
    {
        BOOL nativeBool = new BOOL(ordinal);
        bool managedBool = nativeBool;
        BOOL roundtrippedNativeBool = managedBool;
        Assert.Equal(nativeBool, roundtrippedNativeBool);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void NotLossyConversionBetweenBoolAndBOOL_Ctors(int ordinal)
    {
        BOOL nativeBool = new BOOL(ordinal);
        bool managedBool = nativeBool;
        BOOL roundtrippedNativeBool = new BOOL(managedBool);
        Assert.Equal(nativeBool, roundtrippedNativeBool);
    }

    [Fact]
    public void BOOLEqualsComparesExactValue()
    {
        BOOL b1 = new BOOL(1);
        BOOL b2 = new BOOL(2);
        Assert.Equal(b1, b1);
        Assert.NotEqual(b1, b2);
    }

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
    public void BOOL_OverridesEqualityOperator()
    {
        var @true = new BOOL(true);
        var @false = new BOOL(false);
        Assert.True(@true == new BOOL(true));
        Assert.False(@true != new BOOL(true));
        Assert.True(@true != @false);
        Assert.False(@true == @false);
    }

    [Fact]
    public void CreateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using var fileHandle = PInvoke.CreateFile(
            path,
            FILE_ACCESS_FLAGS.FILE_GENERIC_WRITE,
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
        Assert.Equal(3u, header.dwReserved._1);
#endif

        header.dwReserved.ItemRef(2) = 4;
        Assert.Equal(4u, header.dwReserved.ReadOnlyItemRef(2));
        Assert.Equal(4u, header.dwReserved._2);
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
        info.strServiceShortName._0 = 'H';
        info.strServiceShortName._1 = 'i';
        Assert.Equal("Hi", info.strServiceShortName.ToString(2));
        Assert.Equal("Hi\0\0", info.strServiceShortName.ToString(4));
    }

    [Fact]
    public unsafe void FixedCharArray_ToString()
    {
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO.__char_64 fixedCharArray = default;
        Assert.Equal(string.Empty, fixedCharArray.ToString());
        fixedCharArray._0 = 'H';
        Assert.Equal("H", fixedCharArray.ToString());
        fixedCharArray._1 = 'i';
        Assert.Equal("Hi", fixedCharArray.ToString());

        char* p = &fixedCharArray._0;
        for (int i = 0; i < fixedCharArray.Length; i++)
        {
            *(p + i) = 'x';
        }

        Assert.Equal(new string('x', fixedCharArray.Length), fixedCharArray.ToString());
    }

    [Fact]
    public void FixedLengthArray_ToArray()
    {
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO.__char_64 fixedCharArray = default;
        fixedCharArray = "hi";
        char[] expected = new char[fixedCharArray.Length];
        expected[0] = fixedCharArray._0;
        expected[1] = fixedCharArray._1;
        char[] actual = fixedCharArray.ToArray();
        Assert.Equal<char>(expected, actual);

        actual = fixedCharArray.ToArray(3);
        Assert.Equal<char>(expected.Take(3), actual);
    }

    [Fact]
    public void FixedLengthArray_CopyTo()
    {
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO.__char_64 fixedCharArray = default;
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
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO.__char_64 fixedCharArray = default;
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
        Assert.True(fixedCharArray.Equals(buffer.AsSpan(0, 3)));

        // This should be false because the remainder of the fixed length array is non-default.
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 1)));
        Assert.False(fixedCharArray.Equals(buffer.AsSpan(0, 0)));
    }

    [Fact]
    public void FixedCharArraySetWithString()
    {
        Windows.Win32.System.RestartManager.RM_PROCESS_INFO.__char_64 fixedCharArray = default;

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
        Assert.Equal(128 * sizeof(char), Marshal.SizeOf<WER_REPORT_INFORMATION.__char_128>());
        Assert.Equal(sizeof(char), Marshal.SizeOf<KEY_EVENT_RECORD._uChar_e__Union>());
    }

    [Fact]
    public void CHAR_MarshaledAsUtf8()
    {
        Assert.Equal(1, Marshal.SizeOf<CHAR>());
    }
}
