// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
/// Runtime coverage for the <see cref="ComOutPtrMarshalling"/> policy that the generic COM output pointer
/// friendly overloads carry.
/// </summary>
[Trait("WindowsOnly", "true")]
public partial class ComOutPtrMarshallingTests
{
    /// <summary>BHID_Stream: binds an <see cref="IStream"/> over the item's contents.</summary>
    private static readonly Guid BHID_Stream = new(0x1cebb3ab, 0x7c10, 0x499a, 0xa4, 0x17, 0x92, 0xca, 0x16, 0xc4, 0xcb, 0x83);

    /// <summary>BHID_StorageItem: binds a Windows Runtime <c>Windows.Storage.IStorageItem</c>.</summary>
    private static readonly Guid BHID_StorageItem = new(0x404e2109, 0x77d2, 0x4699, 0xa5, 0xa0, 0x4f, 0xdf, 0x10, 0xdb, 0x98, 0x37);

    /// <summary>CLSID_ShellLink, a cocreatable class that honors an <c>IID_IUnknown</c> activation request.</summary>
    private static readonly Guid CLSID_ShellLink = new(0x00021401, 0x0000, 0x0000, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

    private static string WinIniPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void Default_ProjectsGeneratedComInterface()
    {
        IShellItem shellItem = CreateShellItem(WinIniPath);

        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream stream);

        Assert.NotNull(stream);
        byte[] buffer = new byte[16];
        stream.Read(buffer, out uint bytesRead);
        Assert.True(bytesRead > 0);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ExplicitComObject_MatchesDefaultForGeneratedComInterface()
    {
        IShellItem shellItem = CreateShellItem(WinIniPath);

        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream stream, ComOutPtrMarshalling.ComObject);

        Assert.NotNull(stream);
        stream.Seek(0, SeekOrigin.Begin, out ulong position);
        Assert.Equal(0UL, position);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ExplicitComObject_WithObject_RequestsIUnknown()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        PInvoke.CoCreateInstance<object>(
            CLSID_ShellLink,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            out object instance,
            ComOutPtrMarshalling.ComObject).ThrowOnFailure();

        Assert.NotNull(instance);

        // The wrapper casts dynamically, so exercise the cast rather than reflection-based assignability.
        IShellLinkW link = (IShellLinkW)instance;
        link.SetDescription("ComOutPtrMarshalling");
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void Default_ProjectsWindowsRuntimeInterface()
    {
        IShellItem shellItem = CreateShellItem(WinIniPath);

        // typeof(IStorageItem) is a C#/WinRT projection, so Default resolves to WindowsRuntime.
        shellItem.BindToHandler<IStorageItem>(null, BHID_StorageItem, out IStorageItem storageItem);

        Assert.NotNull(storageItem);
        Assert.Equal("win.ini", storageItem.Name, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ExplicitWindowsRuntime_WithObject_ProjectsInspectable()
    {
        IShellItem shellItem = CreateShellItem(WinIniPath);

        shellItem.BindToHandler<object>(null, BHID_StorageItem, out object storageItem, ComOutPtrMarshalling.WindowsRuntime);

        Assert.NotNull(storageItem);
        IStorageItem asStorageItem = Assert.IsAssignableFrom<IStorageItem>(storageItem);
        Assert.Equal("win.ini", asStorageItem.Name, ignoreCase: true);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ComObjectUniqueInstance_AllowsDeterministicRelease()
    {
        IShellItem shellItem = CreateShellItem(WinIniPath);

        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream unique, ComOutPtrMarshalling.ComObjectUniqueInstance);

        var wrapper = Assert.IsType<ComObject>((object)unique, exactMatch: false);
        byte[] buffer = new byte[8];
        unique.Read(buffer, out uint bytesRead);
        Assert.True(bytesRead > 0);

        wrapper.FinalRelease();
        Assert.Throws<ObjectDisposedException>(() => unique.Read(buffer, out _));

        // The identity-cached wrapper family is unaffected by the unique wrapper's release.
        shellItem.BindToHandler<IStream>(null, BHID_Stream, out IStream identityCached);
        identityCached.Read(buffer, out uint bytesReadAfterRelease);
        Assert.True(bytesReadAfterRelease > 0);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void WindowsRuntimeWithGeneratedComInterface_ThrowsBeforeNativeCall()
    {
        // A failing path would produce a failed HRESULT rather than an exception, so the throw proves
        // the policy was validated before the native call.
        Assert.Throws<ArgumentException>(() =>
            PInvoke.SHCreateItemFromParsingName<IShellItem>(@"Z:\no\such\path", null, out _, ComOutPtrMarshalling.WindowsRuntime));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ComObjectWithWindowsRuntimeInterface_ThrowsBeforeNativeCall()
    {
        Assert.Throws<ArgumentException>(() =>
            PInvoke.SHCreateItemFromParsingName<IStorageItem>(@"Z:\no\such\path", null, out _, ComOutPtrMarshalling.ComObject));
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void NonInterfaceType_ThrowsBeforeNativeCall()
    {
        Assert.Throws<NotSupportedException>(() =>
            PInvoke.SHCreateItemFromParsingName<string>(@"Z:\no\such\path", null, out _, ComOutPtrMarshalling.Default));
    }

    /// <summary>
    /// Verifies that the raw ABI companion dispatches to a managed <c>[GeneratedComClass]</c> implementation of the
    /// public interface, and that the caller-side projection still produces the requested wrapper.
    /// </summary>
    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ManagedImplementer_IsInvokedThroughRawCompanion()
    {
        ManagedShellItem managed = new(CreateShellItem(WinIniPath));
        StrategyBasedComWrappers comWrappers = new();
        nint ccw = comWrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
        try
        {
            object rcw = comWrappers.GetOrCreateObjectForComInstance(ccw, CreateObjectFlags.UniqueInstance);
            IShellItem proxy = (IShellItem)rcw;

            proxy.BindToHandler<IShellItem>(null, BHID_Stream, out IShellItem bound);

            Assert.Equal(1, managed.BindToHandlerCallCount);
            Assert.NotNull(bound);
            bound.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out PWSTR displayName);
            try
            {
                Assert.Equal("win.ini", displayName.ToString(), ignoreCase: true);
            }
            finally
            {
                unsafe
                {
                    Marshal.FreeCoTaskMem((nint)displayName.Value);
                }
            }
        }
        finally
        {
            Marshal.Release(ccw);
        }
    }

    /// <summary>
    /// Verifies that the historical friendly overload remains callable directly on a managed implementation.
    /// </summary>
    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void ManagedImplementer_CanInvokeCompatibilityOverloadDirectly()
    {
        ManagedShellItem managed = new(CreateShellItem(WinIniPath));

        managed.BindToHandler<IShellItem>(null, BHID_Stream, out IShellItem bound);

        Assert.Equal(1, managed.BindToHandlerCallCount);
        Assert.NotNull(bound);
    }

    private static IShellItem CreateShellItem(string path)
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        Assert.True(File.Exists(path), $"Expected '{path}' to exist on Windows.");
        PInvoke.SHCreateItemFromParsingName<IShellItem>(path, null, out IShellItem shellItem).ThrowOnFailure();
        return shellItem;
    }

    /// <summary>
    /// A managed COM server for <see cref="IShellItem"/> that forwards to a real shell item.
    /// </summary>
    /// <param name="inner">The shell item that satisfies the caller's requested interface.</param>
    [GeneratedComClass]
    private partial class ManagedShellItem(IShellItem inner) : IShellItem
    {
        internal int BindToHandlerCallCount { get; private set; }

        public unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, Guid* riid, out object ppv)
        {
            this.BindToHandlerCallCount++;

            // A managed server does not honor the adjacent riid convention; the caller-side projection
            // performs whatever QueryInterface the requested wrapper needs.
            ppv = inner;
        }

        public void GetParent(out IShellItem ppsi) => throw new NotImplementedException();

        public unsafe void GetDisplayName(SIGDN sigdnName, PWSTR* ppszName) => throw new NotImplementedException();

        public unsafe void GetAttributes(SFGAO_FLAGS sfgaoMask, SFGAO_FLAGS* psfgaoAttribs) => throw new NotImplementedException();

        public void Compare(IShellItem psi, uint hint, out int piOrder) => throw new NotImplementedException();
    }
}
