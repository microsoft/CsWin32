// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security;
using Windows.Win32.System.Console;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.WindowsAndMessaging;

/// <summary>
/// Calls real Win32 APIs through their SafeHandle-returning friendly overloads and verifies whether the returned
/// SafeHandle owns (will release) the native handle.
/// </summary>
/// <remarks>
/// <para>
/// Ownership comes from win32metadata: <c>[DoNotRelease]</c>, <c>[RAIIFree]</c>, or the default release function of
/// the handle type. A wrong annotation makes disposing the SafeHandle release a resource the caller was only lent,
/// such as deleting a bitmap that is still in use, unloading the user's keyboard layout, closing the process's window
/// station, or calling <c>CloseHandle</c> on a GDI handle value (which can close an unrelated kernel handle).
/// These tests exist to catch metadata regressions, so they call the actual APIs rather than synthetic metadata.
/// </para>
/// <para>
/// Where releasing a wrongly owned handle has an observable effect on a resource the test created, the test disposes
/// the SafeHandle and checks that the resource survives. Otherwise, it inspects the SafeHandle's ownership directly,
/// then marks it invalid so that it can never release the handle, even when the assertion fails.
/// </para>
/// </remarks>
[Trait("WindowsOnly", "true")]
public unsafe class HandleOwnershipTests
{
    /// <summary>
    /// The skip reason for tests that verify annotations added by a win32metadata version that CsWin32 doesn't consume yet.
    /// </summary>
    private const string RequiresMetadataUpdate = "Requires the annotations from https://github.com/microsoft/win32metadata/pull/2380";

    /// <summary>
    /// A string resource name that user32 interprets as the integer resource 32512 (<c>IDC_ARROW</c> and <c>IDI_APPLICATION</c>).
    /// </summary>
    private const string SystemResource32512 = "#32512";

    /// <summary>
    /// The private field behind the ownership flag passed to the <see cref="SafeHandle"/> constructor.
    /// There is no public API to read it. The name is the same on .NET Framework and .NET.
    /// </summary>
    private static readonly FieldInfo OwnsHandleField = typeof(SafeHandle).GetField("_ownsHandle", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(SafeHandle), "_ownsHandle");

    [Fact]
    public void GetCurrentProcess_IsBorrowed() => AssertBorrowed(PInvoke.GetCurrentProcess_SafeHandle());

    [Fact]
    public void GetStdHandle_IsBorrowed() => AssertBorrowed(PInvoke.GetStdHandle_SafeHandle(STD_HANDLE.STD_OUTPUT_HANDLE));

    [Fact]
    public void GetProcessHeap_IsBorrowed() => AssertBorrowed(PInvoke.GetProcessHeap_SafeHandle());

    [Fact]
    public void GetModuleHandle_IsBorrowed()
    {
        FreeLibrarySafeHandle kernel32 = PInvoke.GetModuleHandle("kernel32.dll");
        Assert.False(kernel32.IsInvalid);
        AssertBorrowed(kernel32);
    }

    [Fact]
    public void LoadCursor_SharedCursor_IsBorrowed()
    {
        DestroyCursorSafeHandle arrow = PInvoke.LoadCursor(null, SystemResource32512);
        Assert.False(arrow.IsInvalid);
        AssertBorrowed(arrow);
    }

    // The following APIs return handles the caller must release. They guard against over-applying [DoNotRelease].
    [Fact]
    public void CreateRectRgn_IsOwned()
    {
        using DeleteObjectHRGNSafeHandle region = PInvoke.CreateRectRgn_SafeHandle(0, 0, 1, 1);
        Assert.False(region.IsInvalid);
        AssertOwned(region);
    }

    [Fact]
    public void CreatePopupMenu_IsOwned()
    {
        using DestroyMenuSafeHandle menu = PInvoke.CreatePopupMenu_SafeHandle();
        Assert.False(menu.IsInvalid);
        AssertOwned(menu);
    }

    [Fact]
    public void OpenProcessToken_IsOwned()
    {
        using SafeFileHandle process = PInvoke.GetCurrentProcess_SafeHandle();
        Assert.True(PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out SafeFileHandle token));
        using (token)
        {
            AssertOwned(token);
        }
    }

    [Fact]
    public void FindFirstFile_IsOwned()
    {
        using FindCloseSafeHandle find = PInvoke.FindFirstFile(Path.Combine(Environment.SystemDirectory, "kernel32.dll"), out _);
        Assert.False(find.IsInvalid);
        AssertOwned(find);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetCurrentThread_IsBorrowed() => AssertBorrowed(PInvoke.GetCurrentThread_SafeHandle());

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetProcessWindowStation_IsBorrowed()
    {
        CloseWindowStationSafeHandle windowStation = PInvoke.GetProcessWindowStation_SafeHandle();
        Assert.False(windowStation.IsInvalid);
        AssertBorrowed(windowStation);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetThreadDesktop_IsBorrowed()
    {
        CloseDesktopSafeHandle desktop = PInvoke.GetThreadDesktop_SafeHandle(PInvoke.GetCurrentThreadId());
        Assert.False(desktop.IsInvalid);
        AssertBorrowed(desktop);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetKeyboardLayout_IsBorrowed()
    {
        // Releasing this would unload the user's active keyboard layout.
        AssertBorrowed(PInvoke.GetKeyboardLayout_SafeHandle(0));
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetStockObject_IsBorrowed()
    {
        DeleteObjectSafeHandle brush = PInvoke.GetStockObject_SafeHandle(GET_STOCK_OBJECT_FLAGS.WHITE_BRUSH);
        Assert.Equal((uint)OBJ_TYPE.OBJ_BRUSH, PInvoke.GetObjectType(brush));
        AssertBorrowed(brush);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetSysColorBrush_IsBorrowed()
    {
        DeleteObjectHBRUSHSafeHandle brush = PInvoke.GetSysColorBrush_SafeHandle(SYS_COLOR_INDEX.COLOR_WINDOW);
        Assert.False(brush.IsInvalid);
        AssertBorrowed(brush);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void SelectObject_PreviousObjectIsBorrowed()
    {
        WithMemoryDC(dc =>
        {
            using DeleteObjectHBITMAPSafeHandle first = PInvoke.CreateCompatibleBitmap_SafeHandle(dc, 1, 1);
            using DeleteObjectHBITMAPSafeHandle second = PInvoke.CreateCompatibleBitmap_SafeHandle(dc, 1, 1);
            using DeleteObjectSafeHandle original = PInvoke.SelectObject(dc, first);
            using (DeleteObjectSafeHandle previous = PInvoke.SelectObject(dc, second))
            {
                Assert.Equal(first.DangerousGetHandle(), previous.DangerousGetHandle());
            }

            // Disposing SelectObject's return value must not delete the bitmap, which this test still owns.
            BITMAP info;
            Assert.Equal(sizeof(BITMAP), PInvoke.GetObject((HGDIOBJ)first.DangerousGetHandle(), sizeof(BITMAP), &info));
            using DeleteObjectSafeHandle restored = PInvoke.SelectObject(dc, original);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetCurrentObject_IsBorrowed()
    {
        WithMemoryDC(dc =>
        {
            DeleteObjectSafeHandle bitmap = PInvoke.GetCurrentObject_SafeHandle(dc, OBJ_TYPE.OBJ_BITMAP);
            Assert.Equal((uint)OBJ_TYPE.OBJ_BITMAP, PInvoke.GetObjectType(bitmap));
            AssertBorrowed(bitmap);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void SelectPalette_PreviousPaletteIsBorrowed()
    {
        WithMemoryDC(dc =>
        {
            using DeleteObjectSafeHandle defaultPalette = PInvoke.GetStockObject_SafeHandle(GET_STOCK_OBJECT_FLAGS.DEFAULT_PALETTE);
            DeleteObjectHPALETTESafeHandle previous = PInvoke.SelectPalette(dc, defaultPalette, false);
            Assert.False(previous.IsInvalid);
            AssertBorrowed(previous);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetColorSpace_And_SetColorSpace_AreBorrowed()
    {
        WithMemoryDC(dc =>
        {
            DeleteColorSpaceSafeHandle current = PInvoke.GetColorSpace_SafeHandle(dc);
            Assert.False(current.IsInvalid);
            DeleteColorSpaceSafeHandle previous = PInvoke.SetColorSpace(dc, current);
            Assert.Equal(current.DangerousGetHandle(), previous.DangerousGetHandle());
            AssertBorrowed(previous);
            AssertBorrowed(current);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void CopyImage_ReturnedOriginalIsBorrowed()
    {
        DestroyIconSafeHandle icon = PInvoke.LoadIcon(null, SystemResource32512);
        try
        {
            Assert.False(icon.IsInvalid);
            SafeFileHandle copy = PInvoke.CopyImage(icon, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0, IMAGE_FLAGS.LR_COPYRETURNORG);

            // LR_COPYRETURNORG returns the original shared icon, which the system owns.
            // For any other result the right release function depends on the image type, so CloseHandle is never right.
            Assert.Equal(icon.DangerousGetHandle(), copy.DangerousGetHandle());
            AssertBorrowed(copy);
        }
        finally
        {
            // LoadIcon's shared icon must never be destroyed, whatever the metadata says about it.
            icon.SetHandleAsInvalid();
        }
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void LoadIcon_SharedIcon_IsBorrowed()
    {
        DestroyIconSafeHandle icon = PInvoke.LoadIcon(null, SystemResource32512);
        Assert.False(icon.IsInvalid);
        AssertBorrowed(icon);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void LoadImage_SharedIcon_IsBorrowed()
    {
        using FreeLibrarySafeHandle user32 = PInvoke.LoadLibrary("user32.dll");
        SafeFileHandle icon = PInvoke.LoadImage(user32, "#100", GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0, IMAGE_FLAGS.LR_DEFAULTSIZE | IMAGE_FLAGS.LR_SHARED);
        Assert.False(icon.IsInvalid, $"LoadImage failed: {Marshal.GetLastWin32Error()}");
        AssertBorrowed(icon);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetCursor_And_SetCursor_AreBorrowed()
    {
        using DestroyCursorSafeHandle arrow = PInvoke.LoadCursor(null, SystemResource32512);
        DestroyCursorSafeHandle previous = PInvoke.SetCursor(arrow);
        DestroyCursorSafeHandle current = PInvoke.GetCursor_SafeHandle();
        Assert.Equal(arrow.DangerousGetHandle(), current.DangerousGetHandle());
        DestroyCursorSafeHandle restored = PInvoke.SetCursor(previous);
        Assert.Equal(arrow.DangerousGetHandle(), restored.DangerousGetHandle());

        AssertBorrowed(previous);
        AssertBorrowed(current);
        AssertBorrowed(restored);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetSubMenu_IsBorrowed()
    {
        using DestroyMenuSafeHandle parent = PInvoke.CreatePopupMenu_SafeHandle();
        using DestroyMenuSafeHandle child = PInvoke.CreatePopupMenu_SafeHandle();
        HMENU childValue = (HMENU)child.DangerousGetHandle();
        Assert.True(PInvoke.AppendMenu(parent, MENU_ITEM_FLAGS.MF_POPUP, (nuint)childValue.Value, "Child"));
        child.SetHandleAsInvalid(); // The parent now owns the submenu and destroys it along with itself.

        using (DestroyMenuSafeHandle subMenu = PInvoke.GetSubMenu(parent, 0))
        {
            Assert.True(subMenu.DangerousValue == childValue);
        }

        // Disposing GetSubMenu's return value must not destroy the submenu, which still belongs to its parent.
        Assert.True(PInvoke.IsMenu(childValue));
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetMenu_IsBorrowed()
    {
        WithWindow(hwnd =>
        {
            using DestroyMenuSafeHandle menu = PInvoke.CreateMenu_SafeHandle();
            HMENU menuValue = (HMENU)menu.DangerousGetHandle();
            Assert.True(PInvoke.SetMenu(hwnd, menu));
            menu.SetHandleAsInvalid(); // The window now owns the menu and destroys it along with itself.

            using (DestroyMenuSafeHandle windowMenu = PInvoke.GetMenu_SafeHandle(hwnd))
            {
                Assert.True(windowMenu.DangerousValue == menuValue);
            }

            // Disposing GetMenu's return value must not destroy the window's menu.
            Assert.True(PInvoke.IsMenu(menuValue));
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetSystemMenu_IsBorrowed()
    {
        WithWindow(hwnd =>
        {
            HMENU systemMenuValue;
            using (DestroyMenuSafeHandle systemMenu = PInvoke.GetSystemMenu_SafeHandle(hwnd, false))
            {
                Assert.False(systemMenu.IsInvalid);
                systemMenuValue = (HMENU)systemMenu.DangerousGetHandle();
            }

            // Disposing GetSystemMenu's return value must not destroy the window's system menu.
            Assert.True(PInvoke.IsMenu(systemMenuValue));
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetWindowTheme_IsBorrowed()
    {
        WithWindow(hwnd =>
        {
            using CloseThemeDataSafeHandle theme = PInvoke.OpenThemeData(hwnd, "BUTTON");

            // GetWindowTheme returns the handle OpenThemeData most recently returned for the window, which the caller
            // of OpenThemeData owns. Both are null when visual styles are off.
            CloseThemeDataSafeHandle windowTheme = PInvoke.GetWindowTheme_SafeHandle(hwnd);
            Assert.Equal(theme.DangerousGetHandle(), windowTheme.DangerousGetHandle());
            AssertBorrowed(windowTheme);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void GetProp_IsBorrowed_And_RemoveProp_IsNotAutoReleased()
    {
        WithWindow(hwnd =>
        {
            const string PropertyName = "CsWin32.HandleOwnershipTests";
            using DeleteObjectHRGNSafeHandle data = PInvoke.CreateRectRgn_SafeHandle(0, 0, 1, 1);
            Assert.True(PInvoke.SetProp(hwnd, PropertyName, data));

            SafeFileHandle property = PInvoke.GetProp(hwnd, PropertyName);
            Assert.Equal(data.DangerousGetHandle(), property.DangerousGetHandle());
            AssertBorrowed(property);

            // RemoveProp hands the data back to the caller, who must free it with the function that matches its type
            // (DeleteObject, through `data`, in this case). CloseHandle is never correct.
            SafeFileHandle removed = PInvoke.RemoveProp(hwnd, PropertyName);
            Assert.Equal(data.DangerousGetHandle(), removed.DangerousGetHandle());
            AssertNotAutoReleased(removed);
        });
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void FindExecutable_IsBorrowed()
    {
        Span<char> executable = stackalloc char[260];
        FreeLibraryHINSTANCESafeHandle result = PInvoke.FindExecutable(Path.Combine(Environment.SystemDirectory, "cmd.exe"), null, executable);

        // FindExecutable returns an error code (greater than 32 on success), not a module handle.
        Assert.True(result.DangerousGetHandle().ToInt64() > 32, $"FindExecutable failed: {result.DangerousGetHandle()}");
        AssertBorrowed(result);
    }

    [Fact(Skip = RequiresMetadataUpdate)]
    public void OleDuplicateData_IsNotAutoReleased()
    {
        using GlobalFreeSafeHandle source = PInvoke.GlobalAlloc_SafeHandle(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, 16);
        Assert.False(source.IsInvalid);

        SafeFileHandle copy = PInvoke.OleDuplicateData(source, CLIPBOARD_FORMAT.CF_TEXT, GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE);
        IntPtr copyValue = copy.DangerousGetHandle();
        try
        {
            Assert.NotEqual(IntPtr.Zero, copyValue);
            AssertNotAutoReleased(copy);
        }
        finally
        {
            // The caller owns the copy, but must free it with the function that matches its format, not CloseHandle.
            PInvoke.GlobalFree((HGLOBAL)copyValue);
        }
    }

    private static bool OwnsHandle(SafeHandle handle) => (bool)OwnsHandleField.GetValue(handle)!;

    /// <summary>
    /// Asserts that <paramref name="handle"/> will not release the native handle it wraps, then marks it invalid
    /// so that it cannot release the handle even if the assertion fails.
    /// </summary>
    /// <param name="handle">The SafeHandle returned by the API under test.</param>
    private static void AssertBorrowed(SafeHandle handle)
        => AssertDoesNotOwn(handle, $"{handle.GetType().Name} owns a handle that the API only lends to the caller.");

    /// <summary>
    /// Asserts that <paramref name="handle"/> will not release the native handle it wraps, then marks it invalid
    /// so that it cannot release the handle even if the assertion fails.
    /// </summary>
    /// <param name="handle">The SafeHandle returned by the API under test.</param>
    /// <remarks>
    /// Use this for handles the caller owns but must release with a function that depends on what the handle refers to,
    /// so no single release function in the metadata can be right.
    /// </remarks>
    private static void AssertNotAutoReleased(SafeHandle handle)
        => AssertDoesNotOwn(handle, $"{handle.GetType().Name} owns a handle whose release function depends on what the handle refers to.");

    private static void AssertDoesNotOwn(SafeHandle handle, string message)
    {
        try
        {
            Assert.False(OwnsHandle(handle), message);
        }
        finally
        {
            handle.SetHandleAsInvalid();
        }
    }

    /// <summary>
    /// Asserts that <paramref name="handle"/> will release the native handle it wraps.
    /// </summary>
    /// <param name="handle">The SafeHandle returned by the API under test.</param>
    private static void AssertOwned(SafeHandle handle)
        => Assert.True(OwnsHandle(handle), $"{handle.GetType().Name} does not own a handle that the caller must release.");

    /// <summary>
    /// Runs <paramref name="test"/> with a memory device context, then deletes it.
    /// </summary>
    /// <param name="test">The test to run.</param>
    private static void WithMemoryDC(Action<HDC> test)
    {
        HDC dc = PInvoke.CreateCompatibleDC(default);
        Assert.False(dc.IsNull);
        try
        {
            test(dc);
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }

    /// <summary>
    /// Runs <paramref name="test"/> with a hidden top-level window, which (unlike a message-only window) can have
    /// a menu and a system menu, then destroys it.
    /// </summary>
    /// <param name="test">The test to run.</param>
    private static void WithWindow(Action<HWND> test)
    {
        HWND hwnd = PInvoke.CreateWindowEx(0, "STATIC", nameof(HandleOwnershipTests), WINDOW_STYLE.WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, default, null, null, null);
        Assert.False(hwnd.IsNull, $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        try
        {
            test(hwnd);
        }
        finally
        {
            PInvoke.DestroyWindow(hwnd);
        }
    }
}
