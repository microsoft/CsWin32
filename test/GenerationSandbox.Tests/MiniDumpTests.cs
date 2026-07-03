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

    [Fact]
    public unsafe void CallbackParameterIsMarshaled()
    {
        // A non-null CallbackParam is marshaled through a single-element array, which produces
        // a pointer to the struct that the native function dereferences and calls back into.
        // This verifies the array-based projection actually forwards the value (not just null).
        bool callbackInvoked = false;
        MINIDUMP_CALLBACK_ROUTINE callback = (void* param, MINIDUMP_CALLBACK_INPUT* input, MINIDUMP_CALLBACK_OUTPUT* output) =>
        {
            callbackInvoked = true;
            return true;
        };

        var callbackInfo = new MINIDUMP_CALLBACK_INFORMATION
        {
            CallbackRoutine = callback,
            CallbackParam = null,
        };

        using Process process = Process.GetCurrentProcess();
        string dumpPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using FileStream dumpStream = File.Create(dumpPath);
            PInvoke.MiniDumpWriteDump(
                process.SafeHandle,
                (uint)process.Id,
                dumpStream.SafeFileHandle,
                MINIDUMP_TYPE.MiniDumpNormal,
                ExceptionParam: null,
                UserStreamParam: null,
                CallbackParam: callbackInfo);
        }
        finally
        {
            GC.KeepAlive(callback);
            File.Delete(dumpPath);
        }

        Assert.True(callbackInvoked);
    }
}
