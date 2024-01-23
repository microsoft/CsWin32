// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Devices.Usb;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.TabletPC;

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

#if DEBUG
    [Fact]
    public void ThrowWhenSetValueIsOutOfBounds()
    {
        BM_REQUEST_TYPE._BM s = default;
        TestUtils.AssertDebugAssertFailed(() => s.Type = 0b100);
    }

    [Fact]
    public void ThrowWhenSetValueIsOutOfBounds_Signed()
    {
        FLICK_DATA s = default;

        // Assert after each invalid set that what ended up being set did not exceed the bounds of the bitfield.
        TestUtils.AssertDebugAssertFailed(() => s.iFlickDirection = -5);
        Assert.Equal(0, s._bitfield & ~0xe0);

        TestUtils.AssertDebugAssertFailed(() => s.iFlickDirection = 4);
        Assert.Equal(0, s._bitfield & ~0xe0);
    }
#endif

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

    [Fact]
    public void SignedField()
    {
        FLICK_DATA s = default;

        // iFlickDirection: 3 bits => range -4..3
        const int mask = 0b111_00000;
        s.iFlickDirection = -1;
        Assert.Equal(0b111_00000, s._bitfield);
        Assert.Equal(-1, s.iFlickDirection);

        s.iFlickDirection = 1;
        Assert.Equal(0b001_00000, s._bitfield);
        Assert.Equal(1, s.iFlickDirection);

        int oldFieldValue = s._bitfield;
        for (sbyte i = -4; i <= 3; i++)
        {
            // Assert that a valid value is retained via the property.
            s.iFlickDirection = i;
            Assert.Equal(i, s.iFlickDirection);

            // Assert that no other bits were touched.
            Assert.Equal(oldFieldValue & ~mask, s._bitfield & ~mask);
        }

        // Repeat the test, but with all 1s in other locations.
        s._bitfield = unchecked((int)0xffffffff);
        oldFieldValue = s._bitfield;
        for (sbyte i = -4; i <= 3; i++)
        {
            // Assert that a valid value is retained via the property.
            s.iFlickDirection = i;
            Assert.Equal(i, s.iFlickDirection);

            // Assert that no other bits were touched.
            Assert.Equal(oldFieldValue & ~mask, s._bitfield & ~mask);
        }
    }

    [Fact]
    public void SignedField_HasBoolFor1Bit()
    {
        FLICK_DATA s = default;
        Assert.False(s.fMenuModifier);
        s.fMenuModifier = true;
        Assert.Equal(0b10_0000_0000, s._bitfield);
    }
}
