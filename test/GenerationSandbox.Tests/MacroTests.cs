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

    [Fact]
    public void GET_X_LPARAMTest()
    {
        unchecked
        {
            // Test positive coordinates
            Assert.Equal(0, GET_X_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal(1, GET_X_LPARAM((LPARAM)(nint)0x00000001));
            Assert.Equal(0x1234, GET_X_LPARAM((LPARAM)(nint)0x56781234));

            // Test negative coordinates (sign extension from short)
            Assert.Equal(-1, GET_X_LPARAM((LPARAM)(nint)0x0000FFFF));
            Assert.Equal(-32768, GET_X_LPARAM((LPARAM)(nint)0x00008000));

            // Test with various high word values
            Assert.Equal(unchecked((int)(short)0xABCD), GET_X_LPARAM((LPARAM)(nint)0x1234ABCD));
        }
    }

    [Fact]
    public void GET_Y_LPARAMTest()
    {
        unchecked
        {
            // Test positive coordinates
            Assert.Equal(0, GET_Y_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal(1, GET_Y_LPARAM((LPARAM)(nint)0x00010000));
            Assert.Equal(0x5678, GET_Y_LPARAM((LPARAM)(nint)0x56781234));

            // Test negative coordinates (sign extension from short)
            Assert.Equal(-1, GET_Y_LPARAM((LPARAM)(nint)0xFFFF0000));
            Assert.Equal(-32768, GET_Y_LPARAM((LPARAM)(nint)0x80000000));

            // Test with various low word values
            Assert.Equal(0x1234, GET_Y_LPARAM((LPARAM)(nint)0x1234ABCD));
        }
    }

    [Fact]
    public void MAKEPOINTSTest()
    {
        unchecked
        {
            // Test zero coordinates
            var p1 = MAKEPOINTS((LPARAM)(nint)0x00000000);
            Assert.Equal((short)0, p1.x);
            Assert.Equal((short)0, p1.y);

            // Test positive coordinates
            var p2 = MAKEPOINTS((LPARAM)(nint)0x00010002);
            Assert.Equal((short)2, p2.x);
            Assert.Equal((short)1, p2.y);

            var p3 = MAKEPOINTS((LPARAM)(nint)0x56781234);
            Assert.Equal((short)0x1234, p3.x);
            Assert.Equal((short)0x5678, p3.y);

            // Test negative coordinates
            var p4 = MAKEPOINTS((LPARAM)(nint)0xFFFFFFFF);
            Assert.Equal((short)-1, p4.x);
            Assert.Equal((short)-1, p4.y);

            var p5 = MAKEPOINTS((LPARAM)(nint)0x80008000);
            Assert.Equal((short)-32768, p5.x);
            Assert.Equal((short)-32768, p5.y);
        }
    }

    [Fact]
    public void GET_WHEEL_DELTA_WPARAMTest()
    {
        unchecked
        {
            // Test positive wheel delta
            Assert.Equal((short)120, GET_WHEEL_DELTA_WPARAM((WPARAM)0x00780000));
            Assert.Equal((short)240, GET_WHEEL_DELTA_WPARAM((WPARAM)0x00F00000));

            // Test negative wheel delta
            Assert.Equal((short)-120, GET_WHEEL_DELTA_WPARAM((WPARAM)0xFF880000));
            Assert.Equal((short)-240, GET_WHEEL_DELTA_WPARAM((WPARAM)0xFF100000));

            // Test zero delta
            Assert.Equal((short)0, GET_WHEEL_DELTA_WPARAM((WPARAM)0x00000000));

            // Test with non-zero low word (low word should be ignored)
            Assert.Equal((short)120, GET_WHEEL_DELTA_WPARAM((WPARAM)0x0078ABCD));
        }
    }

    [Fact]
    public void GET_APPCOMMAND_LPARAMTest()
    {
        unchecked
        {
            // Test extracting app command (HIWORD & ~FAPPCOMMAND_MASK)
            // FAPPCOMMAND_MASK = 0xF000
            Assert.Equal((short)0, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal((short)1, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0x00010000));
            Assert.Equal((short)0x0FFF, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0x0FFF0000));

            // Test with device bits set (should be masked out)
            Assert.Equal((short)1, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0x10010000));
            Assert.Equal((short)1, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0x20010000));
            Assert.Equal((short)1, GET_APPCOMMAND_LPARAM((LPARAM)(nint)0xF0010000));
        }
    }

    [Fact]
    public void GET_DEVICE_LPARAMTest()
    {
        unchecked
        {
            // Test extracting device type (HIWORD & FAPPCOMMAND_MASK)
            // FAPPCOMMAND_MASK = 0xF000
            Assert.Equal((ushort)0x0000, GET_DEVICE_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal((ushort)0x1000, GET_DEVICE_LPARAM((LPARAM)(nint)0x10000000));
            Assert.Equal((ushort)0x2000, GET_DEVICE_LPARAM((LPARAM)(nint)0x20000000));
            Assert.Equal((ushort)0xF000, GET_DEVICE_LPARAM((LPARAM)(nint)0xF0000000));

            // Test with app command bits set (should be masked out)
            Assert.Equal((ushort)0x1000, GET_DEVICE_LPARAM((LPARAM)(nint)0x10FF0000));
        }
    }

    [Fact]
    public void GET_FLAGS_LPARAMTest()
    {
        unchecked
        {
            // Test extracting flags from low word
            Assert.Equal((ushort)0x0000, GET_FLAGS_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal((ushort)0x0001, GET_FLAGS_LPARAM((LPARAM)(nint)0x00000001));
            Assert.Equal((ushort)0xFFFF, GET_FLAGS_LPARAM((LPARAM)(nint)0x0000FFFF));
            Assert.Equal((ushort)0x1234, GET_FLAGS_LPARAM((LPARAM)(nint)0x56781234));

            // Test that high word is ignored
            Assert.Equal((ushort)0xABCD, GET_FLAGS_LPARAM((LPARAM)(nint)0xFFFFABCD));
        }
    }

    [Fact]
    public void GET_KEYSTATE_LPARAMTest()
    {
        unchecked
        {
            // Test extracting key state from low word
            Assert.Equal((ushort)0x0000, GET_KEYSTATE_LPARAM((LPARAM)(nint)0x00000000));
            Assert.Equal((ushort)0x0001, GET_KEYSTATE_LPARAM((LPARAM)(nint)0x00000001));
            Assert.Equal((ushort)0xFFFF, GET_KEYSTATE_LPARAM((LPARAM)(nint)0x0000FFFF));
            Assert.Equal((ushort)0x0004, GET_KEYSTATE_LPARAM((LPARAM)(nint)0x12340004));

            // Test that high word is ignored
            Assert.Equal((ushort)0x0008, GET_KEYSTATE_LPARAM((LPARAM)(nint)0xFFFF0008));
        }
    }

    [Fact]
    public void GET_KEYSTATE_WPARAMTest()
    {
        unchecked
        {
            // Test extracting key state from low word of WPARAM
            Assert.Equal((ushort)0x0000, GET_KEYSTATE_WPARAM((WPARAM)0x00000000));
            Assert.Equal((ushort)0x0001, GET_KEYSTATE_WPARAM((WPARAM)0x00000001));
            Assert.Equal((ushort)0xFFFF, GET_KEYSTATE_WPARAM((WPARAM)0x0000FFFF));
            Assert.Equal((ushort)0x0008, GET_KEYSTATE_WPARAM((WPARAM)0x12340008));

            // Test that high word is ignored
            Assert.Equal((ushort)0x0004, GET_KEYSTATE_WPARAM((WPARAM)0xFFFF0004));
        }
    }

    [Fact]
    public void GET_NCHITTEST_WPARAMTest()
    {
        unchecked
        {
            // Test extracting hit-test value from low word
            Assert.Equal((short)0, GET_NCHITTEST_WPARAM((WPARAM)0x00000000));
            Assert.Equal((short)1, GET_NCHITTEST_WPARAM((WPARAM)0x00000001));
            Assert.Equal((short)-1, GET_NCHITTEST_WPARAM((WPARAM)0x0000FFFF));
            Assert.Equal((short)0x1234, GET_NCHITTEST_WPARAM((WPARAM)0x56781234));

            // Test that high word is ignored
            Assert.Equal((short)0x5678, GET_NCHITTEST_WPARAM((WPARAM)0xFFFF5678));
        }
    }

    [Fact]
    public void GET_RAWINPUT_CODE_WPARAMTest()
    {
        unchecked
        {
            // Test extracting raw input code (lowest 8 bits only)
            Assert.Equal(0x00u, GET_RAWINPUT_CODE_WPARAM((WPARAM)0x00000000));
            Assert.Equal(0x01u, GET_RAWINPUT_CODE_WPARAM((WPARAM)0x00000001));
            Assert.Equal(0xFFu, GET_RAWINPUT_CODE_WPARAM((WPARAM)0x0000FFFF));
            Assert.Equal(0x12u, GET_RAWINPUT_CODE_WPARAM((WPARAM)0xABCDEF12));

            // Test that only lowest 8 bits are extracted
            Assert.Equal(0xABu, GET_RAWINPUT_CODE_WPARAM((WPARAM)0x123456AB));
            Assert.Equal(0x00u, GET_RAWINPUT_CODE_WPARAM((WPARAM)0xFFFFFF00));
        }
    }
}
