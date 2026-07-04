// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

[Trait("WindowsOnly", "true")]
public class ThreadpoolCallbackTests
{
    [Fact]
    public unsafe void NonNullOptionalNonBlittableStructIsMarshaled()
    {
        // Regression test for https://github.com/microsoft/CsWin32/issues/1739.
        // TP_CALLBACK_ENVIRON_V3 is non-blittable (it contains delegate fields), so in the default
        // marshaling mode CsWin32 exposes the optional pcbe parameter as a nullable value type and
        // forwards a non-null value through a single-element array. This test exercises that array
        // marshaling path end to end: the environment must reach the native threadpool intact for
        // the submitted callback to actually run.
        using ManualResetEventSlim callbackRan = new(false);
        PTP_SIMPLE_CALLBACK callback = (instance, context) => callbackRan.Set();

        TP_CALLBACK_ENVIRON_V3 environment = default;
        environment.Version = 3;
        environment.CallbackPriority = TP_CALLBACK_PRIORITY.TP_CALLBACK_PRIORITY_NORMAL;
        environment.Size = (uint)Marshal.SizeOf<TP_CALLBACK_ENVIRON_V3>();

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
