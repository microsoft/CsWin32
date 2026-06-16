// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134, SA1307, SA1308, SA1202

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32.SafeHandles;
using Windows.System;
using Windows.UI.Composition;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.NetworkManagement.WindowsFirewall;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.WinRT.Composition;
using Windows.Win32.System.Wmi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging; // added for window creation APIs

namespace GenerationSandbox.BuildTask.Tests;

[Trait("WindowsOnly", "true")]
public partial class COMTests(ITestOutputHelper outputHelper)
{
    private delegate void CreateCommittedResourceGenericOverloadCompileDelegate(ID3D12Device device, in D3D12_HEAP_PROPERTIES heapProperties, in D3D12_RESOURCE_DESC resourceDesc);

    private ITestOutputHelper outputHelper = outputHelper;

    [Fact]
    public async Task CanInteropWithICompositorInterop()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        var controller = DispatcherQueueController.CreateOnDedicatedThread();
        TaskCompletionSource<bool> tcs = new();
        controller.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                unsafe // Optional parameters use unsafe
                {
                    Compositor compositor = new();

                    PInvoke.D3D11CreateDevice(
                        null,
                        D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                        HINSTANCE.Null,
                        D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                        null,
                        0,
                        PInvoke.D3D11_SDK_VERSION,
                        out var device,
                        null,
                        out var deviceContext);

                    var interop = (ICompositorInterop)(object)compositor;
                    interop.CreateGraphicsDevice(device, out var graphicsDevice);
                    tcs.SetResult(true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The release pipeline runs in a restricted environment where we fail to create a Compositor.
                // Since this runs fine on local dev machines, we can just suppress this failure.
                tcs.SetResult(false);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!await tcs.Task)
        {
            Assert.Skip("Skipping due to UnauthorizedAccessException.");
        }
    }

    [Fact]
    public void CocreatableClassesWithImplicitInterfaces()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        var shellLinkW = ShellLink.CreateInstance<IShellLinkW>();
        var persistFile = (IPersistFile)shellLinkW;
        Assert.NotNull(persistFile);
    }

    [Fact]
    public void IsSHGetFileInfoEasilyCalled()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        SHFILEINFOW fileInfo = default;
        PInvoke.SHGetFileInfo(
            "c:\\windows\\notepad.exe",
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
            MemoryMarshal.AsBytes(new Span<SHFILEINFOW>(ref fileInfo)),
            SHGFI_FLAGS.SHGFI_DISPLAYNAME);
    }

    [Fact]
    public void ID3D12Device_CreateCommittedResource_GenericOverloadsCompile()
    {
        CreateCommittedResourceGenericOverloadCompileDelegate compileOnly = CompileOnlyCreateCommittedResourceGenericOverload;
        Assert.NotNull(compileOnly);
    }

    private static void CompileOnlyCreateCommittedResourceGenericOverload(ID3D12Device device, in D3D12_HEAP_PROPERTIES heapProperties, in D3D12_RESOURCE_DESC resourceDesc)
    {
        device.CreateCommittedResource<ID3D12Resource>(
            in heapProperties,
            D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
            in resourceDesc,
            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
            null,
            out ID3D12Resource resource);
        GC.KeepAlive(resource);
    }


    [Fact]
    [Trait("TestCategory", "RequiresHardware")] // Excluded from the locked-down ADO 1ES build pool; runs on GitHub Actions (Direct2D uses WARP).
    public void ReturnValueMarshalsCorrectly()
    {
        // Create an ID2D1HwndRenderTarget and verify GetHwnd returns the original HWND.
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        // 1. Create the Direct2D factory.
        D2D1_FACTORY_OPTIONS options = new() { debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_NONE };
        PInvoke.D2D1CreateFactory(
            D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
            typeof(ID2D1Factory).GUID,
            options,
            out object objFactory).ThrowOnFailure();
        ID2D1Factory factory = (ID2D1Factory)objFactory;

        // 2. Register a simple window class and create a window.
        HWND hwnd;
        unsafe
        {
            fixed (char* className = "CsWin32TestWindow")
            {
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                static LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
                {
                    return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
                }

                WNDCLASSEXW wc = new()
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    lpszClassName = className,
                };
                var atom = PInvoke.RegisterClassEx(in wc);
                Assert.NotEqual<ushort>(0, atom);

                hwnd = PInvoke.CreateWindowEx(
                    0,
                    "CsWin32TestWindow",
                    "TestD2DWindow",
                    WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                    0,
                    0,
                    32,
                    32,
                    HWND.Null,
                    null,
                    null,
                    null);
                Assert.False(hwnd.IsNull);
            }
        }

        // 3. Prepare render target properties.
        D2D1_RENDER_TARGET_PROPERTIES rtProps = new()
        {
            // Use WARP (software) rendering so this exercises the same D2D render-target
            // creation and HWND marshaling code paths on GPU-less CI VMs without a hardware GPU.
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_SOFTWARE,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_UNKNOWN,
            },
            dpiX = 96.0f,
            dpiY = 96.0f,
            usage = D2D1_RENDER_TARGET_USAGE.D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel = D2D1_FEATURE_LEVEL.D2D1_FEATURE_LEVEL_DEFAULT,
        };

        D2D1_HWND_RENDER_TARGET_PROPERTIES hwndProps = new()
        {
            hwnd = hwnd,
            pixelSize = new D2D_SIZE_U { width = 32, height = 32 },
            presentOptions = D2D1_PRESENT_OPTIONS.D2D1_PRESENT_OPTIONS_NONE,
        };

        // 4. Create the HWND render target.
        factory.CreateHwndRenderTarget(in rtProps, in hwndProps, out ID2D1HwndRenderTarget renderTarget);

        // 5. Retrieve HWND from render target and validate.
        HWND hwndReturned = renderTarget.GetHwnd();
        Assert.Equal(hwnd, hwndReturned);

        D2D_SIZE_U sizeReturned = renderTarget.GetPixelSize();
        Assert.Equal(sizeReturned, hwndProps.pixelSize);

        if (!hwnd.IsNull)
        {
            PInvoke.DestroyWindow(hwnd);
        }
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")] // Excluded from the locked-down ADO 1ES build pool; runs on GitHub Actions.
    public void IWbemServices_GetObject_Works()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        // CoCreateInstance CLSID_WbemLocator
        IWbemLocator locator = WbemLocator.CreateInstance<IWbemLocator>();

        var ns = new SysFreeStringSafeHandle(Marshal.StringToBSTR(@"ROOT\Microsoft\Windows\Defender"), true);
        locator.ConnectServer(ns, new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), 0, new SafeFileHandle(), null, out IWbemServices services);
        Assert.NotNull(services);

        unsafe
        {
            PInvoke.CoSetProxyBlanket(
                    services,
                    10, // RPC_C_AUTHN_WINNT is 10
                    0,  // RPC_C_AUTHZ_NONE is 0
                    pServerPrincName: null,
                    dwAuthnLevel: RPC_C_AUTHN_LEVEL.RPC_C_AUTHN_LEVEL_CALL,
                    dwImpLevel: RPC_C_IMP_LEVEL.RPC_C_IMP_LEVEL_IMPERSONATE,
                    pAuthInfo: null,
                    dwCapabilities: EOLE_AUTHENTICATION_CAPABILITIES.EOAC_NONE);
        }

        var className = new SysFreeStringSafeHandle(Marshal.StringToBSTR("MSFT_MpScan"), true);
        IWbemClassObject? classObj = null; // out param
        services.GetObject(className, WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_RETURN_WBEM_COMPLETE, null, ref classObj, ref Unsafe.NullRef<IWbemCallResult>());

        classObj.GetMethod("Start", 0, out IWbemClassObject pInParamsSignature, out IWbemClassObject ppOutSignature);

        Assert.NotNull(pInParamsSignature);
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void CanCallINetFwMgrApis()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        var fwMgr = NetFwMgr.CreateInstance<INetFwMgr>();
        var authorizedApplications = fwMgr.get_LocalPolicy().get_CurrentProfile().get_AuthorizedApplications();

        var aaObjects = new ComVariant[authorizedApplications.get_Count()];

        var applicationsEnum = (IEnumVARIANT)authorizedApplications.get__NewEnum();
        applicationsEnum.Next((uint)authorizedApplications.get_Count(), aaObjects, out uint fetched);

        foreach (var aaObject in aaObjects)
        {
            var app = (INetFwAuthorizedApplication)ComVariantMarshaller.ConvertToManaged(aaObject)!;

            this.outputHelper.WriteLine("---");
            this.outputHelper.WriteLine($"Name: {app.get_Name().ToString()}");
            this.outputHelper.WriteLine($"Enabled: {(bool)app.get_Enabled()}");
            this.outputHelper.WriteLine($"Remote Addresses: {app.get_RemoteAddresses().ToString()}");
            this.outputHelper.WriteLine($"Scope: {app.get_Scope()}");
            this.outputHelper.WriteLine($"Process Image Filename: {app.get_ProcessImageFileName().ToString()}");
            this.outputHelper.WriteLine($"IP Version: {app.get_IpVersion()}");

            aaObject.Dispose();
        }
    }

    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public unsafe void CanCallPnPAPIs()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        using SafeHandle hDevInfo = PInvoke.SetupDiGetClassDevs(
            Flags: SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);

        var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };

        uint index = 0;
        while (PInvoke.SetupDiEnumDeviceInfo(hDevInfo, index++, ref devInfo))
        {
            // NOTE: Omitting DeviceInstanceId requires naming the RequiredSize parameter.
            PInvoke.SetupDiGetDeviceInstanceId(hDevInfo, in devInfo, RequiredSize: out uint requiredSize);

            Span<char> instanceIdSpan = new char[(int)requiredSize];
            PInvoke.SetupDiGetDeviceInstanceId(hDevInfo, in devInfo, instanceIdSpan);

            this.outputHelper.WriteLine($"Device {devInfo.ClassGuid} Instance ID: {instanceIdSpan.ToString()}");
        }
    }

    /// <summary>
    /// Regression test for <see href="https://github.com/microsoft/CsWin32/issues/1716">#1716</see>.
    /// Calls <see cref="IStream.Read"/> via the friendly Span overload on an IStream obtained from
    /// <see cref="IShellItem.BindToHandler"/> when CsWin32 is configured with <c>useComSourceGenerators</c>.
    /// Shell-returned IStream objects do not honor QueryInterface for <c>IID_ISequentialStream</c>, so the
    /// friendly overload must dispatch via the derived IStream interface rather than upcasting to
    /// ISequentialStream.
    /// </summary>
    [Fact]
    [Trait("TestCategory", "RequiresHardware")]
    public void IShellItem_BindToHandler_IStream_ReadWorks()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        // {1CEBB3AB-7C10-499a-A417-92CA16C4CB83}
        Guid bhidStream = new Guid(0x1cebb3ab, 0x7c10, 0x499a, 0xa4, 0x17, 0x92, 0xca, 0x16, 0xc4, 0xcb, 0x83);

        string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");
        Assert.True(File.Exists(filePath), $"Expected '{filePath}' to exist on Windows.");

        unsafe
        {
            PInvoke.SHCreateItemFromParsingName<IShellItem>(filePath, null, out IShellItem shellItem).ThrowOnFailure();
            shellItem.BindToHandler<IStream>(null, bhidStream, out IStream stream);

            // Friendly Span overload — the original repro for #1716. In source-generator mode this used to
            // throw InvalidCastException because the extension method's `this` parameter was typed as
            // ISequentialStream, forcing an upcast that triggered QI for IID_ISequentialStream on the
            // Shell-returned IStream (which fails).
            byte[] buffer = new byte[16];
            stream.Read(buffer, out uint bytesRead);

            Assert.True(bytesRead > 0, "Expected to read at least one byte from win.ini.");

            // Also call methods declared on IStream itself (Seek and Stat) to verify that adding friendly
            // overloads for inherited methods did not perturb the IStream interface's vtable layout.
            stream.Seek(0, System.IO.SeekOrigin.Begin, out ulong newPosition);
            Assert.Equal(0UL, newPosition);

            STATSTG stat;
            stream.Stat(&stat, STATFLAG.STATFLAG_NONAME);
            Assert.True(stat.cbSize > 0, "Expected Stat to report a non-empty file size.");

            // Read again after Seek to confirm the seek took effect on the same vtable slot.
            stream.Read(buffer, out uint bytesReadAfterSeek);
            Assert.Equal(bytesRead, bytesReadAfterSeek);
        }
    }
}
