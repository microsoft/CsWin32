// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005

using Windows.Win32;
using Windows.Win32.System.Com;

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
#endif
}
