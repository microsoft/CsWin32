// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
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
    public void CreateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        HANDLE fileHandle = PInvoke.CreateFile(
            path,
            FILE_ACCESS_FLAGS.FILE_GENERIC_WRITE,
            FILE_SHARE_FLAGS.FILE_SHARE_NONE,
            lpSecurityAttributes: null,
            FILE_CREATE_FLAGS.CREATE_NEW,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_TEMPORARY | (FILE_FLAGS_AND_ATTRIBUTES)FILE_FLAG_DELETE_ON_CLOSE,
            hTemplateFile: default);
        try
        {
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (fileHandle.Value != IntPtr.Zero)
            {
                PInvoke.CloseHandle(fileHandle);
            }
        }

        Assert.False(File.Exists(path));
    }
}
