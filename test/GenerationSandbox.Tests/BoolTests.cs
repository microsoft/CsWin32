// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32.Foundation;

public class BoolTests
{
    [Fact]
    public void Ctor_bool()
    {
        BOOL b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOL));
    }

    [Fact]
    public void Ctor_int()
    {
        Assert.Equal(2, new BOOL(2).Value);
    }

    [Fact]
    public void ExplicitCast()
    {
        Assert.Equal(2, ((BOOL)2).Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xfffff)]
    public void LossyConversionFromBOOLtoBool(int ordinal)
    {
        BOOL nativeBool = new BOOL(ordinal);
        bool managedBool = nativeBool;
        Assert.Equal(ordinal != 0, managedBool);
        BOOLEAN roundtrippedNativeBool = managedBool;
        Assert.Equal(managedBool ? 1 : 0, roundtrippedNativeBool);
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

        var two = new BOOL(2);
        Assert.False(two == @true);
        Assert.True(two != @true);
    }

    [Fact]
    public void LogicalOperators_And()
    {
        BOOL @true = true, @false = false;
        Assert.False(@false && @false);
        Assert.False(@true && @false);
        Assert.True(@true && @true);
    }

    [Fact]
    public void LogicalOperators_Or()
    {
        BOOL @true = true, @false = false;
        Assert.True(@true || @false);
        Assert.False(@false || @false);
        Assert.True(@true || @true);
    }
}
