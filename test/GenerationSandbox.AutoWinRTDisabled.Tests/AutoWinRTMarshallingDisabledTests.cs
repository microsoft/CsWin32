// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace GenerationSandbox.AutoWinRTDisabled.Tests;

/// <summary>
/// Runtime coverage for opting out of automatic Windows Runtime projection.
/// </summary>
[Trait("WindowsOnly", "true")]
public class AutoWinRTMarshallingDisabledTests
{
    private static readonly Guid BHID_StorageItem = new(0x404e2109, 0x77d2, 0x4699, 0xa5, 0xa0, 0x4f, 0xdf, 0x10, 0xdb, 0x98, 0x37);

    /// <summary>
    /// Verifies that disabling automatic Windows Runtime projection preserves the legacy failure.
    /// </summary>
    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void BindToHandler_AutoWinRTMarshallingDisabled_ThrowsInvalidCastException()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");
        PInvoke.SHCreateItemFromParsingName<IShellItem>(path, null, out IShellItem shellItem).ThrowOnFailure();

        Assert.Throws<InvalidCastException>(() =>
            shellItem.BindToHandler<object>(null, BHID_StorageItem, out _));
    }
}
