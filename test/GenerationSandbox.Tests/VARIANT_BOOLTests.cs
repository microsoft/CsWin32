// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;

public class VARIANT_BOOLTests
{
    [Fact]
    public void Ctor_bool()
    {
        VARIANT_BOOL b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(VARIANT_BOOL));
    }

    [Fact]
    public void Ctor_int()
    {
        Assert.Equal(2, new VARIANT_BOOL(2).Value);
    }

    [Fact]
    public void ExplicitCast()
    {
        Assert.Equal(2, ((VARIANT_BOOL)2).Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xfff)]
    public void LossyConversionFromBOOLtoBool(short ordinal)
    {
        VARIANT_BOOL nativeBool = new VARIANT_BOOL(ordinal);
        bool managedBool = nativeBool;
        Assert.Equal(ordinal != 0, managedBool);
        BOOLEAN roundtrippedNativeBool = managedBool;
        Assert.Equal(managedBool ? 1 : 0, roundtrippedNativeBool);
    }

    [Fact]
    public void BOOLEqualsComparesExactValue()
    {
        VARIANT_BOOL b1 = new VARIANT_BOOL(1);
        VARIANT_BOOL b2 = new VARIANT_BOOL(2);
        Assert.Equal(b1, b1);
        Assert.NotEqual(b1, b2);
    }

    [Fact]
    public void BOOL_OverridesEqualityOperator()
    {
        var @true = new VARIANT_BOOL(true);
        var @false = new VARIANT_BOOL(false);
        Assert.True(@true == new VARIANT_BOOL(true));
        Assert.False(@true != new VARIANT_BOOL(true));
        Assert.True(@true != @false);
        Assert.False(@true == @false);

        var two = new VARIANT_BOOL(2);
        Assert.False(two == @true);
        Assert.True(two != @true);
    }

    [Fact]
    public void LogicalOperators_And()
    {
        VARIANT_BOOL @true = true, @false = false;
        Assert.False(@false && @false);
        Assert.False(@true && @false);
        Assert.True(@true && @true);
    }

    [Fact]
    public void LogicalOperators_Or()
    {
        VARIANT_BOOL @true = true, @false = false;
        Assert.True(@true || @false);
        Assert.False(@false || @false);
        Assert.True(@true || @true);
    }
}
