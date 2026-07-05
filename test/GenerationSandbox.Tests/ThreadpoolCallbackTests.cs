// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

[Trait("WindowsOnly", "true")]
public class ThreadpoolCallbackTests
{
    // Regression tests for https://github.com/microsoft/CsWin32/issues/1739.
    // TP_CALLBACK_ENVIRON_V3 is non-blittable (it contains delegate fields), so in the default
    // marshaling mode CsWin32 exposes the optional pcbe parameter as a nullable value type and
    // forwards it to the native method through an array: a non-null value becomes a single-element
    // array and null becomes a null array. These tests exercise both paths end to end by actually
    // submitting work to the native threadpool and confirming the callback runs. Passing null must
    // marshal to a null pointer rather than dereferencing a null reference (the original bug).
    [Fact]
    public void NonNullOptionalNonBlittableStructIsMarshaled()
    {
        TP_CALLBACK_ENVIRON_V3 environment = default;
        environment.Version = 3;
        environment.CallbackPriority = TP_CALLBACK_PRIORITY.TP_CALLBACK_PRIORITY_NORMAL;
        environment.Size = (uint)Marshal.SizeOf<TP_CALLBACK_ENVIRON_V3>();

        AssertCallbackRuns(environment);
    }

    [Fact]
    public void NullOptionalNonBlittableStructDoesNotThrow()
    {
        AssertCallbackRuns(null);
    }

    private static unsafe void AssertCallbackRuns(TP_CALLBACK_ENVIRON_V3? environment)
    {
        using ManualResetEventSlim callbackRan = new(false);
        PTP_SIMPLE_CALLBACK callback = (instance, context) => callbackRan.Set();

        try
        {
            BOOL submitted = PInvoke.TrySubmitThreadpoolCallback(callback, environment);
            Assert.True(submitted, $"TrySubmitThreadpoolCallback failed with error 0x{Marshal.GetLastWin32Error():X}.");
            Assert.True(callbackRan.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken), "The threadpool callback did not run.");
        }
        finally
        {
            GC.KeepAlive(callback);
        }
    }
}
