// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;
using Windows.Win32.System.Wmi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using IServiceProvider = Windows.Win32.System.Com.IServiceProvider;

public class ComRuntimeTests
{
    [Fact]
    [Trait("TestCategory", "FailsInCloudTest")]
    public void RemotableInterface()
    {
        IShellWindows shellWindows = (IShellWindows)new ShellWindows();
        IServiceProvider serviceProvider = (IServiceProvider)shellWindows.FindWindowSW(
            PInvoke.CSIDL_DESKTOP,
            null,
            ShellWindowTypeConstants.SWC_DESKTOP,
            out int hwnd,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
    }

    [Fact]
    [Trait("TestCategory", "FailsInCloudTest")] // D3D APIs fail in cloud VMs
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
                static LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
                {
                    return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
                }

                WNDCLASSEXW wc = new()
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = WndProc,
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
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
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
    [Trait("TestCategory", "FailsInCloudTest")] // WMI APIs don't work in cloud VMs.
    public void IWbemServices_GetObject_Works()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        // CoCreateInstance CLSID_WbemLocator
        IWbemLocator locator = (IWbemLocator)new WbemLocator();

        var ns = new SysFreeStringSafeHandle(Marshal.StringToBSTR(@"ROOT\Microsoft\Windows\Defender"), true);
        locator.ConnectServer(ns, new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), 0, new SafeFileHandle(IntPtr.Zero, false), null, out IWbemServices services);
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
    [Trait("TestCategory", "FailsInCloudTest")] // WMI APIs don't work in cloud VMs.
    public void CanCallIDispatchOnlyMethods()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        var shellWindows = (IShellWindows)new ShellWindows();

        var serviceProvider = (IServiceProvider)shellWindows.FindWindowSW(
            PInvoke.CSIDL_DESKTOP,
            pvarLocRoot: null,
            ShellWindowTypeConstants.SWC_DESKTOP,
            phwnd: out _,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);

        serviceProvider.QueryService(PInvoke.SID_STopLevelBrowser, typeof(IShellBrowser).GUID, out var shellBrowserAsObject);
        var shellBrowser = (IShellBrowser)shellBrowserAsObject;

        shellBrowser.QueryActiveShellView(out var shellView);

        var iid_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");
        shellView.GetItemObject((uint)_SVGIO.SVGIO_BACKGROUND, iid_IDispatch, out var folderViewAsObject);
        var folderView = (IShellFolderViewDual)folderViewAsObject;

        _ = folderView.Application; // Throws InvalidOleVariantTypeException "Specified OLE variant is invalid"
    }
}
