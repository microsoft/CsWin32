// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;

public class BooleanTests
{
    [Fact]
    public void Boolean()
    {
        BOOLEAN b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOLEAN));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0xff)]
    public void NotLossyConversionBetweenBoolAndBOOLEAN(byte ordinal)
    {
        BOOLEAN nativeBool = new BOOLEAN(ordinal);
        bool managedBool = nativeBool;
        BOOLEAN roundtrippedNativeBool = managedBool;
        Assert.Equal(nativeBool, roundtrippedNativeBool);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0xff)]
    public void NotLossyConversionBetweenBoolAndBOOLEAN_Ctors(byte ordinal)
    {
        BOOLEAN nativeBool = new BOOLEAN(ordinal);
        bool managedBool = nativeBool;
        BOOLEAN roundtrippedNativeBool = new BOOLEAN(managedBool);
        Assert.Equal(nativeBool, roundtrippedNativeBool);
    }

    [Fact]
    public void BOOLEANEqualsComparesExactValue()
    {
        BOOLEAN b1 = new BOOLEAN(1);
        BOOLEAN b2 = new BOOLEAN(2);
        Assert.Equal(b1, b1);
        Assert.NotEqual(b1, b2);
    }

    [Fact]
    public void BOOLEAN_OverridesEqualityOperator()
    {
        var @true = new BOOLEAN(true);
        var @false = new BOOLEAN(false);
        Assert.True(@true == new BOOLEAN(true));
        Assert.False(@true != new BOOLEAN(true));
        Assert.True(@true != @false);
        Assert.False(@true == @false);
    }
}
