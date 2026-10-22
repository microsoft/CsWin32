// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Console;

/// <summary>
/// Calls real Win32 APIs through their SafeHandle-returning friendly overloads and verifies whether the returned
/// SafeHandle owns (will release) the native handle.
/// </summary>
/// <remarks>
/// <para>
/// Ownership comes from win32metadata: <c>[DoNotRelease]</c>, <c>[RAIIFree]</c>, or the default release function of
/// the handle type. A wrong annotation makes disposing the SafeHandle release a resource the caller was only lent,
/// or leak one the caller must release. These tests exist to catch metadata regressions, so they call the actual APIs
/// rather than synthetic metadata.
/// </para>
/// <para>
/// Only APIs whose ownership is already correct in the metadata that CsWin32 consumes belong here, so that fixing
/// the metadata never requires changing these tests.
/// </para>
/// </remarks>
[Trait("WindowsOnly", "true")]
public unsafe class HandleOwnershipTests
{
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

    private static bool OwnsHandle(SafeHandle handle) => (bool)OwnsHandleField.GetValue(handle)!;

    /// <summary>
    /// Asserts that <paramref name="handle"/> will not release the native handle it wraps, then marks it invalid
    /// so that it cannot release the handle even if the assertion fails.
    /// </summary>
    /// <param name="handle">The SafeHandle returned by the API under test.</param>
    private static void AssertBorrowed(SafeHandle handle)
    {
        try
        {
            Assert.False(OwnsHandle(handle), $"{handle.GetType().Name} owns a handle that the API only lends to the caller.");
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
}
