// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace GenerationSandbox.BuildTask.Tests;

/// <summary>
/// Runtime coverage for automatic COM and Windows Runtime output projection.
/// </summary>
[Trait("WindowsOnly", "true")]
public partial class ComOutPtrMarshallingTests
{
    private static readonly Guid BHID_Stream = new(0x1cebb3ab, 0x7c10, 0x499a, 0xa4, 0x17, 0x92, 0xca, 0x16, 0xc4, 0xcb, 0x83);
    private static readonly Guid BHID_StorageItem = new(0x404e2109, 0x77d2, 0x4699, 0xa5, 0xa0, 0x4f, 0xdf, 0x10, 0xdb, 0x98, 0x37);

    // CsWin32's IShellItem applies the marshaller under test. This same-IID raw projection keeps ppv as nint
    // so the test can verify the exact interface pointer exposed to an external native caller.
    [GeneratedComInterface]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal partial interface IShellItemRaw
    {
        unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, in Guid riid, out nint ppv);
    }

    private static string WinIniPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void AutomaticProjection_PreservesGeneratedComInterface()
    {
        IShellItem shellItem = CreateShellItem();

        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream stream);

        Assert.NotNull(stream);
        byte[] buffer = new byte[16];
        stream.Read(buffer, out uint bytesRead);
        Assert.True(bytesRead > 0);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void AutomaticProjection_ProjectsWindowsRuntimeInterface()
    {
        IShellItem shellItem = CreateShellItem();

        shellItem.BindToHandler<IStorageItem>(null, BHID_StorageItem, out IStorageItem storageItem);

        Assert.Equal("win.ini", storageItem.Name, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void AutomaticProjection_ProjectsObjectAsConcreteWindowsRuntimeObject()
    {
        IShellItem shellItem = CreateShellItem();

        shellItem.BindToHandler<object>(null, BHID_StorageItem, out object storageItem);

        StorageFile storageFile = Assert.IsType<StorageFile>(storageItem);
        Assert.Equal("win.ini", storageFile.Name, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void AutomaticProjection_FallsBackToComObject()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        PInvoke.CoCreateInstance<object>(
            typeof(ShellLink).GUID,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            out object instance).ThrowOnFailure();

        IShellLinkW link = (IShellLinkW)instance;
        link.SetDescription(nameof(ComOutPtrMarshallingTests.AutomaticProjection_FallsBackToComObject));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public async Task ManagedImplementer_CanReturnWindowsRuntimeObject()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        StorageFile storageFile = await StorageFile.GetFileFromPathAsync(WinIniPath);

        ComOutPtrMarshallingTests.VerifyManagedImplementer<IStorageItem>(
            storageFile,
            WinRT.GuidGenerator.CreateIID(typeof(IStorageItem)),
            BHID_StorageItem,
            storageItem => Assert.Equal("win.ini", storageItem.Name, ignoreCase: true));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ManagedImplementer_CanReturnInspectableComObject()
    {
        IShellItem shellItem = CreateShellItem();
        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream stream);

        ComOutPtrMarshallingTests.VerifyManagedImplementer<IStream>(
            stream,
            typeof(IStream).GUID,
            BHID_Stream,
            returnedStream =>
            {
                byte[] buffer = new byte[8];
                returnedStream.Read(buffer, out uint bytesRead);
                Assert.True(bytesRead > 0);
            });
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ManagedImplementer_CanReturnNonInspectableComObject()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        PInvoke.CoCreateInstance<object>(
            typeof(ShellLink).GUID,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            out object shellLink).ThrowOnFailure();

        ComOutPtrMarshallingTests.VerifyManagedImplementer<IShellLinkW>(
            shellLink,
            typeof(IShellLinkW).GUID,
            BHID_Stream,
            link => link.SetDescription(nameof(ComOutPtrMarshallingTests.ManagedImplementer_CanReturnNonInspectableComObject)));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public unsafe void ManagedImplementer_CanReturnNull()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        ManagedShellItem managed = new(null!);
        StrategyBasedComWrappers comWrappers = new();
        nint ccw = comWrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
        object rcw = comWrappers.GetOrCreateObjectForComInstance(ccw, CreateObjectFlags.UniqueInstance);
        Marshal.Release(ccw);
        try
        {
            IShellItem proxy = (IShellItem)rcw;
            proxy.BindToHandler<object>(null, BHID_StorageItem, out object result);
            Assert.Null(result);

            Guid requestedIid = WinRT.GuidGenerator.CreateIID(typeof(IStorageItem));
            Guid bindHandler = BHID_StorageItem;
            IShellItemRaw rawProxy = (IShellItemRaw)rcw;
            rawProxy.BindToHandler(null!, &bindHandler, in requestedIid, out nint rawResult);
            Assert.Equal(0, rawResult);
            Assert.Equal(2, managed.BindToHandlerCallCount);
        }
        finally
        {
            ((ComObject)rcw).FinalRelease();
        }
    }

    private static IShellItem CreateShellItem()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        Assert.True(File.Exists(WinIniPath), $"Expected '{WinIniPath}' to exist on Windows.");
        PInvoke.SHCreateItemFromParsingName<IShellItem>(WinIniPath, null, out IShellItem shellItem).ThrowOnFailure();
        return shellItem;
    }

    private static unsafe void VerifyManagedImplementer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(object returnedValue, Guid requestedIid, Guid bindHandler, Action<T> exercise)
        where T : class
    {
        ManagedShellItem managed = new(returnedValue);
        StrategyBasedComWrappers comWrappers = new();
        nint ccw = comWrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
        object rcw = comWrappers.GetOrCreateObjectForComInstance(ccw, CreateObjectFlags.UniqueInstance);
        Marshal.Release(ccw);
        try
        {
            IShellItem proxy = (IShellItem)rcw;
            proxy.BindToHandler<T>(null, bindHandler, out T result);
            exercise(result);

            IShellItemRaw rawProxy = (IShellItemRaw)rcw;
            rawProxy.BindToHandler(null!, &bindHandler, in requestedIid, out nint rawResult);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(rawResult, in requestedIid, out nint queriedResult));
                try
                {
                    Assert.Equal(rawResult, queriedResult);
                }
                finally
                {
                    Marshal.Release(queriedResult);
                }
            }
            finally
            {
                Marshal.Release(rawResult);
            }

            Assert.Equal(2, managed.BindToHandlerCallCount);
        }
        finally
        {
            ((ComObject)rcw).FinalRelease();
        }
    }

    [GeneratedComClass]
    private partial class ManagedShellItem(object returnedValue) : IShellItem
    {
        internal int BindToHandlerCallCount { get; private set; }

        public unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, in Guid riid, out object ppv)
        {
            this.BindToHandlerCallCount++;
            ppv = returnedValue;
        }

        public void GetParent(out IShellItem ppsi) => throw new NotImplementedException();

        public unsafe void GetDisplayName(SIGDN sigdnName, PWSTR* ppszName) => throw new NotImplementedException();

        public unsafe void GetAttributes(SFGAO_FLAGS sfgaoMask, SFGAO_FLAGS* psfgaoAttribs) => throw new NotImplementedException();

        public void Compare(IShellItem psi, uint hint, out int piOrder) => throw new NotImplementedException();
    }
}
