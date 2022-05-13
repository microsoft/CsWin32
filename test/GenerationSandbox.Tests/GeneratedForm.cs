// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.ActiveDirectory;
using Windows.Win32.System.Diagnostics.Debug;

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
        FARPROC p = PInvoke.GetProcAddress(default(HINSTANCE), default(PCSTR));
        p = PInvoke.GetProcAddress(default(SafeHandle), null);
    }

    private static void PROC_InSignatureChangedToIntPtr()
    {
        PROC p = PInvoke.wglGetProcAddress(default(PCSTR));
    }

    private static void RegKeyHandle()
    {
        WIN32_ERROR status = PInvoke.RegLoadAppKey(string.Empty, out SafeRegistryHandle handle, 0, 0, 0);
    }

    private static void PreserveSigBasedOnMetadata()
    {
        IDirectorySearch ds = null!;
        HRESULT hr = ds.GetNextRow(0);
    }
}
