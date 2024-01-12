// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Devices.Usb;
using Windows.Win32.UI.Shell;

public class BitFieldTests
{
    [Fact]
    public void Bool()
    {
        SHELLFLAGSTATE s = default;
        Assert.False(s.fNoConfirmRecycle);
        s.fNoConfirmRecycle = true;
        Assert.Equal(0b100, s._bitfield);
        Assert.True(s.fNoConfirmRecycle);

        s._bitfield = unchecked((int)0xffffffff);
        Assert.True(s.fNoConfirmRecycle);
        s.fNoConfirmRecycle = false;
        Assert.Equal(unchecked((int)0xfffffffb), s._bitfield);
    }

    [Fact]
    public void ThrowWhenSetValueIsOutOfBounds()
    {
        BM_REQUEST_TYPE._BM s = default;
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Type = 0b100);
    }

    [Fact]
    public void SetValueMultiBit()
    {
        BM_REQUEST_TYPE._BM s = default;
        s.Type = 0b11;
        Assert.Equal(0b1100000, s._bitfield);
        Assert.Equal(0b11, s.Type);

        s._bitfield = 0xff;
        Assert.Equal(0b11, s.Type);
        s.Type = 0;
        Assert.Equal(0b10011111, s._bitfield);
    }
}
