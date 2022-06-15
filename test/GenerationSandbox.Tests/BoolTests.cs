// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;

public class BoolTests
{
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
    public void BOOL_OverridesEqualityOperator()
    {
        var @true = new BOOL(true);
        var @false = new BOOL(false);
        Assert.True(@true == new BOOL(true));
        Assert.False(@true != new BOOL(true));
        Assert.True(@true != @false);
        Assert.False(@true == @false);
    }
}
