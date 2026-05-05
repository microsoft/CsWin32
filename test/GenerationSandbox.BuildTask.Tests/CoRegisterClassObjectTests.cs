// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402, SA1649, SA1201, SA1204, SA1124, SA1500, SA1505, SA1508, SA1513, SA1116, SA1117, SA1118, IDE0005

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace GenerationSandbox.BuildTask.Tests;

/// <summary>
/// Investigates https://github.com/microsoft/CsWin32/issues/1670:
/// caller reports that PInvoke.CoRegisterClassObject does not actually register
/// their class factory under NativeAOT + DisableRuntimeMarshalling. Tests here
/// exercise the cswin32-generated PInvoke from the customer's perspective and
/// verify whether subsequent CoCreateInstance invokes the registered factory.
/// </summary>
[Trait("WindowsOnly", "true")]
public partial class CoRegisterClassObjectTests
{
    // A CLSID that does not correspond to any real registered class.
    private static readonly Guid TestClsid = new("8E3F1A6C-4D2B-4B6A-9C5C-7C3F8B1D2E4A");

    /// <summary>
    /// Customer-style usage: pass the managed factory object directly to the
    /// generated PInvoke. The source-generated ComInterfaceMarshaller&lt;object&gt;
    /// should call ComWrappers under the hood and pass the correct IUnknown* to
    /// the OS, so a subsequent CoCreateInstance must invoke the factory.
    /// </summary>
    [Fact]
    public void RegisteringManagedFactory_InvokesCreateInstance()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        RunOnSta(() =>
        {
            var factory = new TestClassFactory();
            HRESULT hrRegister = PInvoke.CoRegisterClassObject(
                TestClsid,
                factory, // pass the managed factory directly to the [MarshalAs(Interface)] object parameter
                CLSCTX.CLSCTX_INPROC_SERVER,
                REGCLS.REGCLS_MULTIPLEUSE,
                out uint cookie);
            Assert.True(hrRegister.Succeeded, $"CoRegisterClassObject failed: 0x{(uint)hrRegister.Value:X8}");
            try
            {
                Guid iidIUnknown = new("00000000-0000-0000-C000-000000000046");
                HRESULT hrCreate = PInvoke.CoCreateInstance(
                    TestClsid,
                    null,
                    CLSCTX.CLSCTX_INPROC_SERVER,
                    iidIUnknown,
                    out object instance);
                Assert.True(hrCreate.Succeeded, $"CoCreateInstance failed: 0x{(uint)hrCreate.Value:X8}");
                Assert.Equal(1, factory.CreateInstanceCallCount);
                Assert.NotNull(instance);
            }
            finally
            {
                PInvoke.CoRevokeClassObject(cookie);
            }
        });
    }

    /// <summary>
    /// Reproduces what the issue reporter actually wrote: they obtained an IUnknown* via
    /// ComWrappers themselves and tried to pass that raw pointer through the generated
    /// API. Because the generated parameter is `[MarshalAs(UnmanagedType.Interface)] object`,
    /// the boxed IntPtr gets wrapped *again* by ComInterfaceMarshaller and the registered
    /// IUnknown is not the factory. This is expected behavior and the test documents it.
    /// </summary>
    [Fact]
    public void PassingRawPointerAsObject_DoesNotRegisterFactory()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");
        RunOnSta(() =>
        {
            var factory = new TestClassFactory();
            nint factoryPunk = TestComWrappers.Instance.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
            try
            {
                HRESULT hrRegister = PInvoke.CoRegisterClassObject(
                    TestClsid,
                    factoryPunk, // boxed nint, NOT a managed COM object — the wrong way to call this API
                    CLSCTX.CLSCTX_INPROC_SERVER,
                    REGCLS.REGCLS_MULTIPLEUSE,
                    out uint cookie);

                // Registration "succeeds" but the registered IUnknown is a wrapper around a boxed IntPtr,
                // not the IClassFactory the caller wanted.
                Assert.True(hrRegister.Succeeded);
                try
                {
                    Guid iidIUnknown = new("00000000-0000-0000-C000-000000000046");
                    HRESULT hrCreate = PInvoke.CoCreateInstance(
                        TestClsid,
                        null,
                        CLSCTX.CLSCTX_INPROC_SERVER,
                        iidIUnknown,
                        out object instance);

                    // The factory's CreateInstance is never called because COM never sees the real IClassFactory.
                    Assert.Equal(0, factory.CreateInstanceCallCount);
                    Assert.False(hrCreate.Succeeded, $"Unexpectedly succeeded: 0x{(uint)hrCreate.Value:X8}");
                }
                finally
                {
                    PInvoke.CoRevokeClassObject(cookie);
                }
            }
            finally
            {
                Marshal.Release(factoryPunk);
            }
        });
    }

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            unsafe
            {
                HRESULT hrInit = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (hrInit.Succeeded)
                    {
                        PInvoke.CoUninitialize();
                    }
                }
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    [GeneratedComClass]
    internal partial class TestClassFactory : IClassFactory
    {
        public int CreateInstanceCallCount;

        public int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
        {
            this.CreateInstanceCallCount++;

            var obj = new TestComObject();
            nint punk = TestComWrappers.Instance.GetOrCreateComInterfaceForObject(obj, CreateComInterfaceFlags.None);
            try
            {
                Guid iid = riid;
                return Marshal.QueryInterface(punk, in iid, out ppvObject);
            }
            finally
            {
                Marshal.Release(punk);
            }
        }

        public int LockServer(bool fLock) => 0;
    }

    [GeneratedComClass]
    internal partial class TestComObject : IDummy
    {
    }

    [GeneratedComInterface]
    [Guid("9F11A4FE-5C8B-4A2D-8C3E-EE2A4D67B14C")]
    internal partial interface IDummy
    {
    }

    [GeneratedComInterface]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal partial interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    internal sealed class TestComWrappers : StrategyBasedComWrappers
    {
        internal static readonly TestComWrappers Instance = new();
    }
}
