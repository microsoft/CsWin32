// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Drawing;
using Windows.Win32.Foundation;

public class SystemDrawingStructTests
{
    [Fact]
    public void Size()
    {
        SIZE s = new SIZE(1, 1);
        Size s2 = s;
        Assert.False(s.IsEmpty);
        Assert.False(s2.IsEmpty);

        Assert.True(default(SIZE).IsEmpty);
    }

    [Fact]
    public void NotLossyConversionBetweenSizeAndSIZE()
    {
        SIZE nativeSize = new SIZE(1, 1);
        Size managedSize = nativeSize;
        SIZE roundtrippedNativeSize = managedSize;
        Assert.Equal(nativeSize, roundtrippedNativeSize);
    }

    [Fact]
    public void NotLossyConversionBetweenSizeAndSIZE_Ctors()
    {
        SIZE nativeSize = new SIZE(1, 1);
        Size managedSize = nativeSize;
        SIZE roundtrippedNativeSize = new SIZE(managedSize);
        Assert.Equal(nativeSize, roundtrippedNativeSize);
    }

    [Fact]
    public void Rect()
    {
        RECT r = new RECT(1, 1, 2, 2);
        Rectangle r2 = r;
        Assert.False(r.IsEmpty);
        Assert.False(r2.IsEmpty);

        Assert.True(default(RECT).IsEmpty);
    }

    [Fact]
    public void NotLossyConversionBetweenRectangleAndRECT()
    {
        RECT nativeSize = new RECT(1, 1, 2, 2);
        Rectangle managedSize = nativeSize;
        RECT roundtrippedNativeSize = managedSize;
        Assert.Equal(nativeSize, roundtrippedNativeSize);
    }

    [Fact]
    public void NotLossyConversionBetweenRectangleAndRECT_Ctors()
    {
        RECT nativeSize = new RECT(1, 1, 2, 2);
        Rectangle managedSize = nativeSize;
        RECT roundtrippedNativeSize = new RECT(managedSize);
        Assert.Equal(nativeSize, roundtrippedNativeSize);
    }

    [Fact]
    public void RectangleAndRECTFromXYWH_AreEqual()
    {
        RECT nativeSize = RECT.FromXYWH(1, 1, 2, 2);
        Rectangle managedSize = new Rectangle(1, 1, 2, 2);
        Assert.Equal(nativeSize.left, managedSize.Left);
        Assert.Equal(nativeSize.right, managedSize.Right);
        Assert.Equal(nativeSize.top, managedSize.Top);
        Assert.Equal(nativeSize.bottom, managedSize.Bottom);
    }
}
