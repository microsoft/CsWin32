// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace WinRTInteropTest;

internal class Program
{
    private const int AddButtonId = 1000;
    private const string WindowClassName = "WinRTInteropWindow";

    private static readonly Random Rnd = new();

    private static void Main()
    {
        RegisterWindowClass();
        InitInstance();

        while (GetMessage(out MSG msg, default, 0, 0))
        {
            TranslateMessage(msg);
            DispatchMessage(msg);
        }
    }

    private static void RegisterWindowClass()
    {
        unsafe
        {
            fixed (char* szClassName = WindowClassName)
            {
                var wcex = default(WNDCLASSEXW);
                PCWSTR szNull = default;
                PCWSTR szCursorName = new((char*)IDC_ARROW);
                PCWSTR szIconName = new((char*)IDI_APPLICATION);
                wcex.cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>();
                wcex.lpfnWndProc = WndProc;
                wcex.cbClsExtra = 0;
                wcex.hInstance = GetModuleHandle(szNull);
                wcex.hCursor = LoadCursor(wcex.hInstance, szCursorName);
                wcex.hIcon = LoadIcon(wcex.hInstance, szIconName);
                wcex.hbrBackground = new HBRUSH(new IntPtr(6));
                wcex.lpszClassName = szClassName;
                RegisterClassEx(wcex);
            }
        }
    }

    private static void InitInstance()
    {
        HWND hwnd;
        unsafe
        {
            hwnd =
                CreateWindowEx(
                    0,
                    WindowClassName,
                    "WinRT Interop Test",
                    WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                    CW_USEDEFAULT,
                    0,
                    900,
                    672,
                    default,
                    default,
                    default,
                    null);
        }

        ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_NORMAL);
        UpdateWindow(hwnd);
    }

    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wparam, LPARAM lparam)
    {
        switch (message)
        {
            case WM_CREATE:
                CompositionHost.Instance.Initialize(hwnd);

                unsafe
                {
                    CreateWindowEx(
                        0,
                        "button",
                        "Add element",
                        WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CHILD | BS_PUSHBUTTON,
                        12,
                        12,
                        100,
                        50,
                        hwnd,
                        new NoReleaseSafeHandle(AddButtonId),
                        null,
                        null);
                }

                break;

            case WM_COMMAND:
                int cmdid = (int)(wparam.Value & 0xFFFF);
                switch (cmdid)
                {
                    case AddButtonId:
                        float size = (Rnd.Next() % 150) + 50;
                        float x = Rnd.Next() % 600;
                        float y = Rnd.Next() % 200;
                        CompositionHost.Instance.AddElement(size, x, y);
                        break;

                    default:
                        return DefWindowProc(hwnd, message, wparam, lparam);
                }

                break;

            case WM_PAINT:
                BeginPaint(hwnd, out PAINTSTRUCT ps);

                // More paint code would go here...
                EndPaint(hwnd, ps);
                break;

            case WM_CLOSE:
                DestroyWindow(hwnd);
                break;

            case WM_DESTROY:
                PostQuitMessage(0);
                break;

            default:
                return DefWindowProc(hwnd, message, wparam, lparam);
        }

        return new LRESULT(0);
    }

    private class NoReleaseSafeHandle : SafeHandle
    {
        public NoReleaseSafeHandle(int value)
            : base(IntPtr.Zero, true)
        {
            this.SetHandle(new IntPtr(value));
        }

        public override bool IsInvalid => throw new NotImplementedException();

        protected override bool ReleaseHandle()
        {
            return true;
        }
    }
}
