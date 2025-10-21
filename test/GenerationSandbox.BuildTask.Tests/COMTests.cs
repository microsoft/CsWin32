// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134, SA1307, SA1308

using System.ComponentModel;
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
using Windows.Win32.UI.Shell;

[Trait("WindowsOnly", "true")]
public partial class COMTests
{
    [Fact]
    public async Task CanInteropWithICompositorInterop()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        var controller = DispatcherQueueController.CreateOnDedicatedThread();
        TaskCompletionSource<bool> tcs = new();
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
                    tcs.SetResult(true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The release pipeline runs in a restricted environment where we fail to create a Compositor.
                // Since this runs fine on local dev machines, we can just suppress this failure.
                tcs.SetResult(false);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!await tcs.Task)
        {
            Assert.Skip("Skipping due to UnauthorizedAccessException.");
        }
    }

    [Fact]
    public void CocreatableClassesWithImplicitInterfaces()
    {
        var shellLinkW = ShellLink.CreateInstance<IShellLinkW>();
        var persistFile = (IPersistFile)shellLinkW;
        Assert.NotNull(persistFile);
    }
}

[Guid("00021401-0000-0000-C000-000000000046")]
[global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.217+533aa1bddf.RR")]
internal partial class ShellLink
{
    [Obsolete("COM source generators do not support direct instantiation of co-creatable classes. Use CreateInstance<T> method instead.")]
    public ShellLink() { throw new NotSupportedException("COM source generators do not support direct instantiation of co-creatable classes. Use CreateInstance<T> method instead."); }

    public static T CreateInstance<T>() where T : class
    {
        PInvoke.CoCreateInstance<T>(typeof(ShellLink).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out T ret).ThrowOnFailure();
        return ret;
    }
}

//[Guid("00021401-0000-0000-C000-000000000046")]
//[global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.217+533aa1bddf.RR")]
//internal partial struct ShellLink2
//{
//    public object Instance;

//    public ShellLink2()
//    {
//        PInvoke.CoCreateInstance(typeof(ShellLink).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out Instance).ThrowOnFailure();
//    }

//    public static implicit operator T(in ShellLink2 instance)
//    {
//        return (T)instance.Instance;
//    }
//}
