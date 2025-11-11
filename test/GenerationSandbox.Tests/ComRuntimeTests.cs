// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;
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
}
