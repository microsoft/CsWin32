// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Windows.Sdk;
using Xunit;

[Trait("WindowsOnly", "true")]
public class BasicTests
{
    [Fact]
    public void GetTickCount_Nonzero()
    {
        uint result = PInvoke.GetTickCount();
        Assert.NotEqual(0u, result);
    }

    [Fact]
    public void DISPLAYCONFIG_VIDEO_SIGNAL_INFO_Test()
    {
        DISPLAYCONFIG_VIDEO_SIGNAL_INFO i = default;
        i.pixelRate = 5;

        // TODO: write code that sets/gets memory on the inner struct (e.g. videoStandard).
    }

    [Fact]
    public void Bool()
    {
        BOOL b = true;
        bool b2 = b;
        Assert.True(b);
        Assert.True(b2);

        Assert.False(default(BOOL));
    }
}
