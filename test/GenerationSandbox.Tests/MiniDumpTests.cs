// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;

[Trait("WindowsOnly", "true")]
public class MiniDumpTests
{
    [Fact]
    public void NullOptionalPointerParametersDoNotThrow()
    {
        // Regression test for https://github.com/microsoft/CsWin32/issues/1739.
        // CallbackParam points at MINIDUMP_CALLBACK_INFORMATION, which is non-blittable
        // (it contains a delegate field). Passing null for it must marshal to a null
        // pointer rather than causing the marshaler to dereference a null reference.
        //
        // Note: this deliberately passes null for CallbackParam so that no managed callback
        // runs while the process is being dumped. Supplying a callback here would let dbghelp
        // invoke managed code while other threads are suspended for the dump, which can
        // deadlock; the non-null marshaling is instead verified at runtime by
        // ThreadpoolCallbackTests.NonNullOptionalNonBlittableStructIsMarshaled.
        using Process process = Process.GetCurrentProcess();
        string dumpPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using FileStream dumpStream = File.Create(dumpPath);
            BOOL result = PInvoke.MiniDumpWriteDump(
                process.SafeHandle,
                (uint)process.Id,
                dumpStream.SafeFileHandle,
                MINIDUMP_TYPE.MiniDumpNormal,
                ExceptionParam: null,
                UserStreamParam: null,
                CallbackParam: null);
            Assert.True(result, $"MiniDumpWriteDump failed with error 0x{Marshal.GetLastWin32Error():X}.");
        }
        finally
        {
            File.Delete(dumpPath);
        }
    }
}
