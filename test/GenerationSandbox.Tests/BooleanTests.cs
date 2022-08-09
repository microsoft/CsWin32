// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;

public class BooleanTests
{
    [Fact]
    public void Ctor_Bool()
    {
        BOOLEAN b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOLEAN));
    }

    [Fact]
    public void Ctor_byte()
    {
        Assert.Equal(2, new BOOLEAN(2).Value);
    }

    [Fact]
    public void ExplicitCast()
    {
        Assert.Equal(2, ((BOOLEAN)2).Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0xff)]
    [InlineData(0x80)]
    [InlineData(0x00)]
    [InlineData(0x01)]
    public void LossyConversionFromBOOLEANtoBool(byte ordinal)
    {
        BOOLEAN nativeBool = new BOOLEAN(ordinal);
        bool managedBool = nativeBool;
        Assert.Equal(ordinal != 0, managedBool);
        BOOLEAN roundtrippedNativeBool = managedBool;
        Assert.Equal(managedBool ? 1 : 0, roundtrippedNativeBool);
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

        var two = new BOOLEAN(2);
        Assert.False(two == @true);
        Assert.True(two != @true);
    }

    [Fact]
    public void LogicalOperators_And()
    {
        BOOLEAN @true = true, @false = false;
        Assert.False(@false && @false);
        Assert.False(@true && @false);
        Assert.True(@true && @true);
    }

    [Fact]
    public void LogicalOperators_Or()
    {
        BOOLEAN @true = true, @false = false;
        Assert.True(@true || @false);
        Assert.False(@false || @false);
        Assert.True(@true || @true);
    }
}
