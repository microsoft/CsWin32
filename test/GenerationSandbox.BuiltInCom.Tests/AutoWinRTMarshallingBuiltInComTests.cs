// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace GenerationSandbox.BuiltInCom.Tests;

/// <summary>
/// Runtime coverage for automatic Windows Runtime projection with built-in COM interop.
/// </summary>
[Trait("WindowsOnly", "true")]
public class AutoWinRTMarshallingBuiltInComTests
{
    private static readonly Guid BHID_StorageItem = new(0x404e2109, 0x77d2, 0x4699, 0xa5, 0xa0, 0x4f, 0xdf, 0x10, 0xdb, 0x98, 0x37);
    private static readonly Guid IID_IShellItem = new(0x43826d1e, 0xe718, 0x42ee, 0xbc, 0x55, 0xa1, 0xe2, 0x61, 0xc3, 0x7b, 0xfe);

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void BindToHandler_ProjectsWindowsRuntimeInterface()
    {
        IShellItem shellItem = AutoWinRTMarshallingBuiltInComTests.CreateShellItem();

        shellItem.BindToHandler<IStorageItem>(null, BHID_StorageItem, out IStorageItem storageItem);

        Assert.Equal("win.ini", storageItem.Name, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void BindToHandler_ProjectsObjectAsWindowsRuntimeObject()
    {
        IShellItem shellItem = AutoWinRTMarshallingBuiltInComTests.CreateShellItem();

        shellItem.BindToHandler<object>(null, BHID_StorageItem, out object storageItem);

        IStorageItem projected = Assert.IsAssignableFrom<IStorageItem>(storageItem);
        Assert.Equal("win.ini", projected.Name, ignoreCase: true);
    }

    private static IShellItem CreateShellItem()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, in IID_IShellItem, out IShellItem shellItem));
        return shellItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);
}
