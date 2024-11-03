// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

public class MacroTests
{
    [Fact]
    public void HRESULT_FROM_WIN32Test()
    {
        Assert.Equal(HRESULT.S_OK, HRESULT_FROM_WIN32(WIN32_ERROR.NO_ERROR));
    }

    [Fact]
    public void MAKELONGTest()
    {
        Assert.Equal(0x00000000u, MAKELONG(0x0000, 0x0000));
        Assert.Equal(0x00010000u, MAKELONG(0x0000, 0x0001));
        Assert.Equal(0x00000001u, MAKELONG(0x0001, 0x0000));
        Assert.Equal(0x00010002u, MAKELONG(0x0002, 0x0001));
        Assert.Equal(0xFFFF0000u, MAKELONG(0x0000, 0xFFFF));
        Assert.Equal(0x0000FFFFu, MAKELONG(0xFFFF, 0x0000));
        Assert.Equal(0xFFFFFFFFu, MAKELONG(0xFFFF, 0xFFFF));
    }

    [Fact]
    public void MAKEWPARAMTest()
    {
        Assert.Equal(0x00000000u, MAKEWPARAM(0x0000, 0x0000));
        Assert.Equal(0x00010000u, MAKEWPARAM(0x0000, 0x0001));
        Assert.Equal(0x00000001u, MAKEWPARAM(0x0001, 0x0000));
        Assert.Equal(0x00010002u, MAKEWPARAM(0x0002, 0x0001));
        Assert.Equal(0xFFFF0000u, MAKEWPARAM(0x0000, 0xFFFF));
        Assert.Equal(0x0000FFFFu, MAKEWPARAM(0xFFFF, 0x0000));
        Assert.Equal(0xFFFFFFFFu, MAKEWPARAM(0xFFFF, 0xFFFF));
    }

    [Fact]
    public void MAKELPARAMTest()
    {
        unchecked
        {
            Assert.Equal((LPARAM)(nint)0x00000000, MAKELPARAM(0x0000, 0x0000));
            Assert.Equal((LPARAM)(nint)0x00010000, MAKELPARAM(0x0000, 0x0001));
            Assert.Equal((LPARAM)(nint)0x00000001, MAKELPARAM(0x0001, 0x0000));
            Assert.Equal((LPARAM)(nint)0x00010002, MAKELPARAM(0x0002, 0x0001));
            Assert.Equal((LPARAM)(nint)0xFFFF0000, MAKELPARAM(0x0000, 0xFFFF));
            Assert.Equal((LPARAM)(nint)0x0000FFFF, MAKELPARAM(0xFFFF, 0x0000));
            Assert.Equal((LPARAM)(nint)0xFFFFFFFF, MAKELPARAM(0xFFFF, 0xFFFF));
        }
    }

    [Fact]
    public void MAKELRESULTTest()
    {
        unchecked
        {
            Assert.Equal((LRESULT)(nint)0x00000000, MAKELRESULT(0x0000, 0x0000));
            Assert.Equal((LRESULT)(nint)0x00010000, MAKELRESULT(0x0000, 0x0001));
            Assert.Equal((LRESULT)(nint)0x00000001, MAKELRESULT(0x0001, 0x0000));
            Assert.Equal((LRESULT)(nint)0x00010002, MAKELRESULT(0x0002, 0x0001));
            Assert.Equal((LRESULT)(nint)0xFFFF0000, MAKELRESULT(0x0000, 0xFFFF));
            Assert.Equal((LRESULT)(nint)0x0000FFFF, MAKELRESULT(0xFFFF, 0x0000));
            Assert.Equal((LRESULT)(nint)0xFFFFFFFF, MAKELRESULT(0xFFFF, 0xFFFF));
        }
    }

    [Fact]
    public void LOWORDTest()
    {
        Assert.Equal((ushort)0x0000, LOWORD(0x00000000u));
        Assert.Equal((ushort)0x0001, LOWORD(0x00010001u));
        Assert.Equal((ushort)0x0000, LOWORD(0xFFFF0000u));
        Assert.Equal((ushort)0x1234, LOWORD(0x56781234u));
        Assert.Equal((ushort)0xFFFF, LOWORD(0xFFFFFFFFu));
        Assert.Equal((ushort)0xABCD, LOWORD(0x1234ABCDu));
        Assert.Equal((ushort)0x0000, LOWORD(0xFFFF0000u));
        Assert.Equal((ushort)0x8000, LOWORD(0xFFFF8000u));
    }

    [Fact]
    public void HIWORDTest()
    {
        Assert.Equal((ushort)0x0000, HIWORD(0x00000000u));
        Assert.Equal((ushort)0x0001, HIWORD(0x00010001u));
        Assert.Equal((ushort)0xFFFF, HIWORD(0xFFFF0000u));
        Assert.Equal((ushort)0x5678, HIWORD(0x56781234u));
        Assert.Equal((ushort)0xFFFF, HIWORD(0xFFFFFFFFu));
        Assert.Equal((ushort)0x1234, HIWORD(0x1234ABCDu));
        Assert.Equal((ushort)0xFFFF, HIWORD(0xFFFF1234u));
        Assert.Equal((ushort)0x8000, HIWORD(0x80000000u));
    }
}
