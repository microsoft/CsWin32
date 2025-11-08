// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134, SA1307, SA1308, SA1202

using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Composition;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT.Composition;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging; // added for window creation APIs

namespace GenerationSandbox.BuildTask.Tests;

[Trait("WindowsOnly", "true")]
public partial class COMTests
{
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

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
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
                    0, 0, 32, 32,
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
