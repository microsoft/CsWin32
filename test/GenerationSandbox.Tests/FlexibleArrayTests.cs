// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32.System.Ole;

public class FlexibleArrayTests
{
    [Fact]
    public unsafe void FlexibleArraySizing()
    {
        const int count = 3;
        PAGESET* pPageSet = (PAGESET*)Marshal.AllocHGlobal(PAGESET.SizeOf(count));
        try
        {
            pPageSet->rgPages[0].nFromPage = 0;

            Span<PAGERANGE> pageRange = pPageSet->rgPages.AsSpan(count);
            for (int i = 0; i < count; i++)
            {
                pageRange[i].nFromPage = i * 2;
                pageRange[i].nToPage = (i * 2) + 1;
            }
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)pPageSet);
        }
    }

    [Fact]
    public void SizeOf_Minimum1Element()
    {
        Assert.Equal(PAGESET.SizeOf(1), PAGESET.SizeOf(0));
        Assert.Equal(Marshal.SizeOf<PAGERANGE>(), PAGESET.SizeOf(2) - PAGESET.SizeOf(1));
    }
}
