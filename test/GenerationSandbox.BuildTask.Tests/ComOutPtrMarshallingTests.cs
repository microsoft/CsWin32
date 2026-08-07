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
using WinRT;

namespace GenerationSandbox.BuildTask.Tests;

/// <summary>
/// Runtime coverage for automatic COM and Windows Runtime object marshalling.
/// </summary>
[Trait("WindowsOnly", "true")]
public partial class ComOutPtrMarshallingTests
{
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    private static readonly Guid BHID_Stream = new(0x1cebb3ab, 0x7c10, 0x499a, 0xa4, 0x17, 0x92, 0xca, 0x16, 0xc4, 0xcb, 0x83);
    private static readonly Guid BHID_StorageItem = new(0x404e2109, 0x77d2, 0x4699, 0xa5, 0xa0, 0x4f, 0xdf, 0x10, 0xdb, 0x98, 0x37);

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
    public async Task WindowsRuntimeObject_CanBePassedAsComInput()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        StorageFile storageFile = await StorageFile.GetFileFromPathAsync(WinIniPath);
        IWinRTObject provider = (IWinRTObject)(object)storageFile;

        unsafe
        {
            _ = PInvoke.CoAllowSetForegroundWindow(provider, null);
        }

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

        AssertRcwIdentityIsPreserved(shellLink);

        ComOutPtrMarshallingTests.VerifyManagedImplementer<IShellLinkW>(
            shellLink,
            BHID_Stream,
            link => link.SetDescription(nameof(ComOutPtrMarshallingTests.ManagedImplementer_CanReturnNonInspectableComObject)));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ManagedImplementer_CanReturnNull()
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

            Assert.Equal(1, managed.BindToHandlerCallCount);
        }
        finally
        {
            ((ComObject)rcw).FinalRelease();
        }
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public async Task CsWinRTRcw_RoundTripsWithOriginalIdentity()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        StorageFile storageFile = await StorageFile.GetFileFromPathAsync(WinIniPath);
        IWinRTObject winrtObject = (IWinRTObject)(object)storageFile;

        nint marshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(storageFile);
        try
        {
            AssertSameComIdentity(winrtObject.NativeObject.ThisPtr, marshalled);

            object roundTripped = ComOrWinRTObjectMarshaller.ConvertToManaged(marshalled);
            IStorageItem storageItem = Assert.IsAssignableFrom<IStorageItem>(roundTripped);
            Assert.Equal("win.ini", storageItem.Name, ignoreCase: true);
        }
        finally
        {
            ComOrWinRTObjectMarshaller.Free(marshalled);
        }
    }

    [Fact]
    public void ManagedWinRTObject_RoundTripsThroughWinRTCcw()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        DisposableObject managed = new();

        nint marshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(managed);
        try
        {
            AssertSupportsInterface(marshalled, typeof(WinRT.IInspectable).GUID);
            AssertSupportsInterface(marshalled, GuidGenerator.CreateIID(typeof(IDisposable)));

            object roundTripped = ComOrWinRTObjectMarshaller.ConvertToManaged(marshalled);
            Assert.Same(managed, roundTripped);
            IDisposable disposable = Assert.IsAssignableFrom<IDisposable>(roundTripped);
            disposable.Dispose();
            Assert.Equal(1, managed.DisposeCallCount);

            nint remarshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(managed);
            try
            {
                AssertSameComIdentity(marshalled, remarshalled);
            }
            finally
            {
                ComOrWinRTObjectMarshaller.Free(remarshalled);
            }
        }
        finally
        {
            ComOrWinRTObjectMarshaller.Free(marshalled);
        }
    }

    [Fact]
    public void GeneratedComClass_RoundTripsThroughGeneratedComCcw()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        ManagedShellItem managed = new(null!);

        nint marshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(managed);
        try
        {
            AssertSupportsInterface(marshalled, typeof(IShellItem).GUID);
            AssertDoesNotSupportInterface(marshalled, typeof(WinRT.IInspectable).GUID);

            object roundTripped = ComOrWinRTObjectMarshaller.ConvertToManaged(marshalled);
            Assert.Same(managed, roundTripped);
            IShellItem shellItem = Assert.IsAssignableFrom<IShellItem>(roundTripped);
            shellItem.Compare(shellItem, 0, out int order);
            Assert.Equal(42, order);
            Assert.Equal(1, managed.CompareCallCount);
        }
        finally
        {
            ComOrWinRTObjectMarshaller.Free(marshalled);
        }
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ClassicComRcw_RoundTripsWithOriginalIdentity()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        Type shellLinkType = Type.GetTypeFromCLSID(typeof(ShellLink).GUID, throwOnError: true)!;
        object shellLink = Activator.CreateInstance(shellLinkType)!;
        try
        {
            Assert.True(Marshal.IsComObject(shellLink));
            nint expected = Marshal.GetIUnknownForObject(shellLink);
            try
            {
                nint marshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(shellLink);
                try
                {
                    AssertSameComIdentity(expected, marshalled);
                    AssertSupportsInterface(marshalled, typeof(IShellLinkW).GUID);
                }
                finally
                {
                    ComOrWinRTObjectMarshaller.Free(marshalled);
                }
            }
            finally
            {
                Marshal.Release(expected);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static IShellItem CreateShellItem()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        Assert.True(File.Exists(WinIniPath), $"Expected '{WinIniPath}' to exist on Windows.");
        PInvoke.SHCreateItemFromParsingName<IShellItem>(WinIniPath, null, out IShellItem shellItem).ThrowOnFailure();
        return shellItem;
    }

    private static void AssertSameComIdentity(nint expected, nint actual)
    {
        Guid iid = typeof(IUnknown).GUID;
        nint expectedIdentity = QueryInterface(expected, iid);
        try
        {
            nint actualIdentity = QueryInterface(actual, iid);
            try
            {
                Assert.Equal(expectedIdentity, actualIdentity);
            }
            finally
            {
                Marshal.Release(actualIdentity);
            }
        }
        finally
        {
            Marshal.Release(expectedIdentity);
        }
    }

    private static void AssertRcwIdentityIsPreserved(object value)
    {
        Assert.True(ComWrappers.TryGetComInstance(value, out nint native));
        try
        {
            nint marshalled = ComOrWinRTObjectMarshaller.ConvertToUnmanaged(value);
            try
            {
                AssertSameComIdentity(native, marshalled);
            }
            finally
            {
                ComOrWinRTObjectMarshaller.Free(marshalled);
            }
        }
        finally
        {
            Marshal.Release(native);
        }
    }

    private static void AssertSupportsInterface(nint value, Guid iid)
    {
        nint queried = QueryInterface(value, iid);
        Marshal.Release(queried);
    }

    private static void AssertDoesNotSupportInterface(nint value, Guid iid)
    {
        int hr = Marshal.QueryInterface(value, in iid, out nint queried);
        if (queried != 0)
        {
            Marshal.Release(queried);
        }

        Assert.Equal(E_NOINTERFACE, hr);
    }

    private static nint QueryInterface(nint value, Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(value, in iid, out nint queried));
        return queried;
    }

    private static void VerifyManagedImplementer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(object returnedValue, Guid bindHandler, Action<T> exercise)
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

            Assert.Equal(1, managed.BindToHandlerCallCount);
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

        internal int CompareCallCount { get; private set; }

        public unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, Guid* riid, out object ppv)
        {
            this.BindToHandlerCallCount++;
            ppv = returnedValue;
        }

        public void GetParent(out IShellItem ppsi) => throw new NotImplementedException();

        public unsafe void GetDisplayName(SIGDN sigdnName, PWSTR* ppszName) => throw new NotImplementedException();

        public unsafe void GetAttributes(SFGAO_FLAGS sfgaoMask, SFGAO_FLAGS* psfgaoAttribs) => throw new NotImplementedException();

        public void Compare(IShellItem psi, uint hint, out int piOrder)
        {
            this.CompareCallCount++;
            piOrder = 42;
        }
    }

    private sealed class DisposableObject : IDisposable
    {
        internal int DisposeCallCount { get; private set; }

        public void Dispose() => this.DisposeCallCount++;
    }
}
