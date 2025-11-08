// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005,SA1202

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

public class COMTests
{
#if NET7_0_OR_GREATER
    [Fact]
    public void COMStaticGuid()
    {
        Assert.Equal(typeof(IPersistFile).GUID, IPersistFile.IID_Guid);
        Assert.Equal(typeof(IPersistFile).GUID, GetGuid<IPersistFile>());
    }

    private static Guid GetGuid<T>()
        where T : IComIID
        => T.Guid;

    [Trait("WindowsOnly", "true")]
    [Fact]
    public unsafe void CocreatableClassesWithImplicitInterfaces()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        ShellLink.CreateInstance(out IShellLinkW* shellLinkWPtr).ThrowOnFailure();
        shellLinkWPtr->QueryInterface(typeof(IPersistFile).GUID, out void* ppv).ThrowOnFailure();
        IPersistFile* persistFilePtr = (IPersistFile*)ppv;
        Assert.NotNull(persistFilePtr);
        persistFilePtr->Release();
        shellLinkWPtr->Release();
    }
#endif

    private delegate LRESULT WndProcDelegate(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam);

#if NET7_0_OR_GREATER
    [Fact]
    public unsafe void ReturnValueMarshalsCorrectly()
    {
        // Create an ID2D1HwndRenderTarget and verify GetHwnd returns the original HWND.
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        // 1. Create the Direct2D factory.
        D2D1_FACTORY_OPTIONS options = new() { debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_NONE };
        PInvoke.D2D1CreateFactory(
            D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
            typeof(ID2D1Factory).GUID,
            options,
            out var ppv).ThrowOnFailure();
        ID2D1Factory* factory = (ID2D1Factory*)ppv;

#if NET7_0_OR_GREATER
            [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
#endif
        static LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
        {
            return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // 2. Register a simple window class and create a window.
        HWND hwnd;
#if NET472_OR_GREATER
        var wndproc = new WndProcDelegate(WndProc);
#endif
        fixed (char* className = "CsWin32TestWindow")
        {
            WNDCLASSEXW wc = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
#if NET7_0_OR_GREATER
                lpfnWndProc = &WndProc,
#else
                lpfnWndProc = (delegate* unmanaged[Stdcall]<global::Windows.Win32.Foundation.HWND, uint, global::Windows.Win32.Foundation.WPARAM, global::Windows.Win32.Foundation.LPARAM, global::Windows.Win32.Foundation.LRESULT>)Marshal.GetFunctionPointerForDelegate(wndproc),
#endif
                lpszClassName = className,
            };
            var atom = PInvoke.RegisterClassEx(&wc);
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
        ID2D1HwndRenderTarget* renderTarget;
        factory->CreateHwndRenderTarget(in rtProps, in hwndProps, &renderTarget);

        // 5. Retrieve HWND from render target and validate.
        HWND hwndReturned = renderTarget->GetHwnd();
        Assert.Equal(hwnd, hwndReturned);

#if NET7_0_OR_GREATER // BUG: net472 doesn't support MemberFunction, so there's no way to get the return type right. See https://github.com/microsoft/CsWin32/issues/1479
        D2D_SIZE_U sizeReturned = renderTarget->GetPixelSize();
        Assert.Equal(sizeReturned, hwndProps.pixelSize);
#endif

        if (!hwnd.IsNull)
        {
            PInvoke.DestroyWindow(hwnd);
        }

        renderTarget->Release();
        factory->Release();
    }
#endif
}
