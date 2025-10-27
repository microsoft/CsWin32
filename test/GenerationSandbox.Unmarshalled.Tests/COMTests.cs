// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005,SA1202

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

public class COMTests
{
#if NET7_0_OR_GREATER
    [Fact]
    public void COMStaticGuid()
    {
        Assert.Equal(typeof(IPersistFile).GUID, IPersistFile.IID_Guid);
        Assert.Equal(typeof(IPersistFile).GUID, GetGuid<IPersistFile>());
    }

    private static Guid GetGuid<T>()
        where T : IComIID
        => T.Guid;

    [Trait("WindowsOnly", "true")]
    [Fact]
    public unsafe void CocreatableClassesWithImplicitInterfaces()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test calls Windows-specific APIs");

        ShellLink.CreateInstance(out IShellLinkW* shellLinkWPtr).ThrowOnFailure();
        shellLinkWPtr->QueryInterface(typeof(IPersistFile).GUID, out void* ppv).ThrowOnFailure();
        IPersistFile* persistFilePtr = (IPersistFile*)ppv;
        Assert.NotNull(persistFilePtr);
        persistFilePtr->Release();
        shellLinkWPtr->Release();
    }
#endif
}
