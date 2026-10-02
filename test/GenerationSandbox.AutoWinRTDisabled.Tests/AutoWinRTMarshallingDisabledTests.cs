// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace GenerationSandbox.AutoWinRTDisabled.Tests;

/// <summary>
/// Runtime coverage for opting out of automatic Windows Runtime projection.
/// </summary>
[Trait("WindowsOnly", "true")]
public partial class AutoWinRTMarshallingDisabledTests
{
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

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

    [Fact]
    public void IClassFactory_ManagedImplementerReturnsRequestedInterface()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        ManagedClassFactory factory = new();

        factory.CreateInstance<IClassFactory>(null, out IClassFactory result);
        result.LockServer(true);

        Assert.Equal(1, factory.CreateInstanceCallCount);
        Assert.Equal(1, factory.LockServerCallCount);
    }

    [Fact]
    public void IClassFactory_ManagedImplementerRejectsUnsupportedInterface()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        ManagedClassFactory factory = new();

        InvalidCastException exception = Assert.Throws<InvalidCastException>(() => factory.CreateInstance<IShellItem>(null, out _));

        Assert.Equal(E_NOINTERFACE, exception.HResult);
        Assert.Equal(1, factory.CreateInstanceCallCount);
    }

    [GeneratedComClass]
    private sealed partial class ManagedClassFactory : IClassFactory
    {
        internal int CreateInstanceCallCount { get; private set; }

        internal int LockServerCallCount { get; private set; }

        public unsafe void CreateInstance(object pUnkOuter, Guid* riid, out void* ppvObject)
        {
            this.CreateInstanceCallCount++;
            ppvObject = ComOutPtr.FromManaged(this, in *riid);
        }

        public void LockServer(BOOL fLock) => this.LockServerCallCount++;
    }
}
