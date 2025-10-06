// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134, SA1307, SA1308

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Composition;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT.Composition;

public partial class COMTests
{
    [Fact]
    public async Task CanInteropWithICompositorInterop()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread();
            TaskCompletionSource tcs = new();
            controller.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    unsafe // Optional parameters use unsafe
                    {
                        Compositor compositor = new();

                        PInvoke.D3D11CreateDevice(
                            null,
                            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                            HINSTANCE.Null,
                            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                            null,
                            0,
                            PInvoke.D3D11_SDK_VERSION,
                            out var device,
                            null,
                            out var deviceContext);

                        var interop = (ICompositorInterop)(object)compositor;
                        interop.CreateGraphicsDevice(device, out var graphicsDevice);
                        tcs.SetResult();
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            await tcs.Task;
        }
    }
}
