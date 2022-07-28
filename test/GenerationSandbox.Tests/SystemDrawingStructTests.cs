// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

public class SystemDrawingStructTests
{
    [Fact]
    public void Point()
    {
        POINT p = new POINT(1, 1);
        Point p2 = p;
        Assert.False(p.IsEmpty);
        Assert.False(p2.IsEmpty);

        Assert.True(default(POINT).IsEmpty);
    }

    [Fact]
    public void NotLossyConversionBetweenPointAndPOINT()
    {
        POINT nativePoint = new POINT(1, 1);
        Point managedPoint = nativePoint;
        POINT roundtrippedNativePoint = managedPoint;
        Assert.Equal(nativePoint, roundtrippedNativePoint);
    }

    [Fact]
    public void NotLossyConversionBetweenPointAndPOINT_Ctors()
    {
        POINT nativePoint = new POINT(1, 1);
        Point managedPoint = nativePoint;
        POINT roundtrippedNativePoint = new POINT(managedPoint);
        Assert.Equal(nativePoint, roundtrippedNativePoint);
    }

    [Fact]
    public void PointF()
    {
        POINTF p = new POINTF(1, 1);
        PointF p2 = p;
        Assert.False(p.IsEmpty);
        Assert.False(p2.IsEmpty);

        Assert.True(default(POINTF).IsEmpty);
    }

    [Fact]
    public void NotLossyConversionBetweenPointFAndPOINTF()
    {
        POINTF nativePoint = new POINTF(1, 1);
        PointF managedPoint = nativePoint;
        POINTF roundtrippedNativePoint = managedPoint;
        Assert.Equal(nativePoint, roundtrippedNativePoint);
    }

    [Fact]
    public void NotLossyConversionBetweenPointFAndPOINTF_Ctors()
    {
        POINTF nativePoint = new POINTF(1, 1);
        PointF managedPoint = nativePoint;
        POINTF roundtrippedNativePoint = new POINTF(managedPoint);
        Assert.Equal(nativePoint, roundtrippedNativePoint);
    }

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
