// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Windows.Sdk;
using Xunit;

[Trait("WindowsOnly", "true")]
public class BasicTests
{
    private const int FILE_FLAG_DELETE_ON_CLOSE = 0x04000000; // remove when https://github.com/microsoft/win32metadata/issues/98 is fixed.

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

    [Fact]
    public void Bool()
    {
        BOOL b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOL));
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
    public void CreateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using var fileHandle = PInvoke.CreateFile(
            path,
            FILE_ACCESS_FLAGS.FILE_GENERIC_WRITE,
            FILE_SHARE_FLAGS.FILE_SHARE_NONE,
            lpSecurityAttributes: null,
            FILE_CREATE_FLAGS.CREATE_NEW,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_TEMPORARY | (FILE_FLAGS_AND_ATTRIBUTES)FILE_FLAG_DELETE_ON_CLOSE,
            hTemplateFile: NullSafeHandle.NullHandle);
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
}
