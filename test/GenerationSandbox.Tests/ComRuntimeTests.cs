// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32;
using Windows.Win32.UI.Shell;
using IServiceProvider = Windows.Win32.System.Com.IServiceProvider;

public class ComRuntimeTests
{
    [Fact]
    [Trait("TestCategory", "FailsInCloudTest")]
    public void RemotableInterface()
    {
        IShellWindows shellWindows = (IShellWindows)new ShellWindows();
        IServiceProvider serviceProvider = (IServiceProvider)shellWindows.FindWindowSW(
            PInvoke.CSIDL_DESKTOP,
            null,
            ShellWindowTypeConstants.SWC_DESKTOP,
            out int hwnd,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
    }
}
