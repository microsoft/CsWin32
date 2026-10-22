// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;

/// <summary>
/// Calls real Win32 APIs whose handle values have the high bit set, through the SafeHandle-returning
/// friendly overloads, in a project that has <c>CheckForOverflowUnderflow</c> enabled.
/// </summary>
/// <remarks>
/// <para>
/// Native handles are pointer-sized values, not addresses. Several common ones have the high bit set when viewed
/// as a native integer: <c>INVALID_HANDLE_VALUE</c> (-1), the <c>GetCurrentProcess</c> (-1) and
/// <c>GetCurrentThread</c> (-2) pseudo-handles, and 32-bit GDI/USER handles that are sign-extended to 64 bits.
/// Converting such a value between <c>void*</c> and <see cref="IntPtr"/> with a checked C# cast throws
/// <see cref="OverflowException"/>, so all generated conversions must be unchecked.
/// See https://github.com/microsoft/CsWin32/issues/1412.
/// </para>
/// </remarks>
[Trait("WindowsOnly", "true")]
public class SignExtendedHandleTests
{
    /// <summary>
    /// GDI handle values are recycled with an incrementing uniqueness counter in their upper bits, so creating and
    /// deleting objects in a loop yields a sign-extended handle within a few hundred iterations.
    /// This cap only bounds the loop if that assumption ever stops holding.
    /// </summary>
    private const int MaxGdiAttempts = 10_000;

    [Fact]
    public void GetCurrentProcess_SafeHandle_PseudoHandleIsUsable()
    {
        using SafeFileHandle process = PInvoke.GetCurrentProcess_SafeHandle();

        Assert.Equal(new IntPtr(-1), process.DangerousGetHandle());
        Assert.Equal((uint)Process.GetCurrentProcess().Id, PInvoke.GetProcessId(process));

        Assert.True(PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out SafeFileHandle token), $"OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
        using (token)
        {
            Assert.False(token.IsInvalid);
        }
    }

    [Fact]
    public void GetCurrentThread_SafeHandle_PseudoHandleIsUsable()
    {
        SafeFileHandle thread = PInvoke.GetCurrentThread_SafeHandle();
        try
        {
            Assert.Equal(new IntPtr(-2), thread.DangerousGetHandle());
            Assert.Equal(PInvoke.GetCurrentThreadId(), PInvoke.GetThreadId(thread));
        }
        finally
        {
            // The pseudo-handle must never be closed. HandleOwnershipTests verifies the ownership annotation.
            thread.SetHandleAsInvalid();
        }
    }

    [Fact]
    public void CreateFile_Failure_ReturnsInvalidHandle()
    {
        string missingFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using SafeFileHandle file = PInvoke.CreateFile(
            missingFile,
            (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ,
            FILE_SHARE_MODE.FILE_SHARE_READ,
            lpSecurityAttributes: null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
            hTemplateFile: null);
        int lastError = Marshal.GetLastWin32Error();

        Assert.True(file.IsInvalid);
        Assert.Equal(new IntPtr(-1), file.DangerousGetHandle());
        Assert.Equal((int)WIN32_ERROR.ERROR_FILE_NOT_FOUND, lastError);
    }

    [Fact]
    public void FindFirstFile_NoMatch_ReturnsInvalidHandle()
    {
        string missingDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using FindCloseSafeHandle find = PInvoke.FindFirstFile(Path.Combine(missingDirectory, "*"), out _);

        Assert.True(find.IsInvalid);
        Assert.Equal(new IntPtr(-1), find.DangerousGetHandle());
    }

    [Fact]
    public void GdiBitmaps_SignExtendedHandles_RoundTripThroughSafeHandles()
    {
        HDC memoryDC = PInvoke.CreateCompatibleDC(default);
        Assert.False(memoryDC.IsNull);
        try
        {
            // GDI handles are 32-bit values that are sign-extended on 64-bit Windows. The upper bits hold a reuse
            // counter, so creating and deleting objects soon produces one with the high bit set.
            // Bitmaps are used rather than regions because gdi32 caches deleted regions and hands back the same handle.
            bool sawSignExtendedBitmap = false;
            for (int i = 0; i < MaxGdiAttempts && !sawSignExtendedBitmap; i++)
            {
                sawSignExtendedBitmap = CreateUseAndDeleteBitmap(memoryDC);
            }

            // If this fails, the test never exercised a sign-extended handle, so it proves nothing.
            Assert.True(sawSignExtendedBitmap, "No sign-extended HBITMAP was observed.");
        }
        finally
        {
            PInvoke.DeleteDC(memoryDC);
        }
    }

    /// <summary>
    /// Exercises the generated <c>Marshal.InitHandle</c> path, <c>DangerousValue</c>, <c>Lease</c>, a
    /// <see cref="SafeHandle"/> parameter, and <c>ReleaseHandle</c> for a bitmap.
    /// </summary>
    /// <param name="memoryDC">The device context the bitmap is compatible with.</param>
    /// <returns><see langword="true"/> if the bitmap's handle was sign-extended.</returns>
    private static unsafe bool CreateUseAndDeleteBitmap(HDC memoryDC)
    {
        HBITMAP value;
        BITMAP info;
        using (DeleteObjectHBITMAPSafeHandle bitmap = PInvoke.CreateCompatibleBitmap_SafeHandle(memoryDC, 1, 1))
        {
            Assert.False(bitmap.IsInvalid);
            value = (HBITMAP)bitmap.DangerousGetHandle();
            Assert.True(bitmap.DangerousValue == value);

            using (DeleteObjectHBITMAPSafeHandle.LeaseScope lease = bitmap.Lease())
            {
                Assert.True(lease.Value == value);
            }

            Assert.Equal((uint)OBJ_TYPE.OBJ_BITMAP, PInvoke.GetObjectType(bitmap));
            Assert.Equal(sizeof(BITMAP), PInvoke.GetObject((HGDIOBJ)value, sizeof(BITMAP), &info));
        }

        // Disposal must have deleted the bitmap, not just returned without throwing.
        // GetObjectType can still report a type for a deleted handle, but GetObject fails.
        Assert.Equal(0, PInvoke.GetObject((HGDIOBJ)value, sizeof(BITMAP), &info));
        return ((IntPtr)value).ToInt64() < 0;
    }
}
