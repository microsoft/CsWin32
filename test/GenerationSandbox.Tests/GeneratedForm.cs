// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.ActiveDirectory;
using Windows.Win32.System.Com;
using Windows.Win32.System.Diagnostics.Debug;
using Windows.Win32.System.RestartManager;
using Windows.Win32.System.Threading;

#pragma warning disable CA1812 // dead code

/// <summary>
/// Contains "tests" that never run. Merely compiling is enough to verify the generated code has the right API shape.
/// </summary>
internal static unsafe class GeneratedForm
{
    private static void IEnumDebugPropertyInfo(IEnumDebugPropertyInfo info)
    {
        var span = new DebugPropertyInfo[2];
        HRESULT result = info.Next(span, out uint initialized);
        info.Clone(out IEnumDebugPropertyInfo ppepi);
    }

    private static void LPARAM_From_NInt()
    {
        LPARAM p = 1;
    }

    private static void WPARAM_From_NInt()
    {
        WPARAM p = 1;
    }

    private static void FARPROC_InSignatureChangedToIntPtr()
    {
        FARPROC p = PInvoke.GetProcAddress(default(HMODULE), default(PCSTR));
        p = PInvoke.GetProcAddress(default(SafeHandle), null);
    }

    private static void PROC_InSignatureChangedToIntPtr()
    {
        PROC p = PInvoke.wglGetProcAddress(default(PCSTR));
    }

    private static void RegKeyHandle()
    {
        WIN32_ERROR status = PInvoke.RegLoadAppKey(string.Empty, out SafeRegistryHandle handle, 0, 0);
    }

    private static void PreserveSigBasedOnMetadata()
    {
        IDirectorySearch ds = null!;
        HRESULT hr = ds.GetNextRow(default(ADS_SEARCH_HANDLE));
    }

    private static unsafe void OverlappedAPIs()
    {
        NativeOverlapped overlapped = default;
        PInvoke.WriteFile(default(HANDLE), null, 0, null, &overlapped);
    }

    private static void ZZStringUsed()
    {
        Windows.Win32.UI.Shell.SHFILEOPSTRUCTW s = default;
        PCZZWSTR from = s.pFrom;
    }

    private static void PROCESS_BASIC_INFORMATION_PebBaseAddressIsPointer()
    {
        PROCESS_BASIC_INFORMATION info = default;
        PEB_unmanaged* p = null;
        info.PebBaseAddress = p;
    }

    private static void WriteFile()
    {
        uint written = 0;
        PInvoke.WriteFile((SafeHandle?)null, new byte[2], &written, (NativeOverlapped*)null);
    }

    private static void RmRegisterResources()
    {
        PInvoke.RmRegisterResources(0, ["a", "b"], [default(RM_UNIQUE_PROCESS)], ["a", "b"]);
    }

    private class MyStream : IStream
    {
        public HRESULT Read(void* pv, uint cb, uint* pcbRead)
        {
            // the last parameter should be a pointer because it is optional, and using `out uint` makes it very cumbersome
            // for C# implementation of this interface to detect a null argument before setting the out parameter,
            // which would throw NRE in such a case.
            throw new NotImplementedException();
        }

        public HRESULT Write(void* pv, uint cb, uint* pcbWritten)
        {
            throw new NotImplementedException();
        }

        public void Seek(long dlibMove, SeekOrigin dwOrigin, ulong* plibNewPosition)
        {
            throw new NotImplementedException();
        }

        public void SetSize(ulong libNewSize)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(IStream pstm, ulong cb, ulong* pcbRead, ulong* pcbWritten)
        {
            throw new NotImplementedException();
        }

        public void Commit([MarshalAs(UnmanagedType.U4)] STGC grfCommitFlags)
        {
            throw new NotImplementedException();
        }

        public void Revert()
        {
            throw new NotImplementedException();
        }

        public void LockRegion(ulong libOffset, ulong cb, [MarshalAs(UnmanagedType.U4)] LOCKTYPE dwLockType)
        {
            throw new NotImplementedException();
        }

        public void UnlockRegion(ulong libOffset, ulong cb, uint dwLockType)
        {
            throw new NotImplementedException();
        }

        public void Stat(Windows.Win32.System.Com.STATSTG* pstatstg, [MarshalAs(UnmanagedType.U4)] STATFLAG grfStatFlag)
        {
            throw new NotImplementedException();
        }

        public void Clone(out IStream ppstm)
        {
            throw new NotImplementedException();
        }
    }
}
