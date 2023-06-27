// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable CA1812 // dead code
#pragma warning disable CS0168 // Variable is declared but never used

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.Events;
using Windows.Win32.System.Variant;

/// <summary>
/// Contains "tests" that never run. Merely compiling is enough to verify the generated code has the right API shape.
/// </summary>
internal static unsafe class GeneratedForm
{
    private static unsafe void COMStructsPreserveSig()
    {
        IEventSubscription o = default;

        // Default is non-preservesig
        VARIANT v = o.GetPublisherProperty(null);
        BSTR bstr = o.MethodName;

        // NativeMethods.json opts into PreserveSig for these particular methods.
        HRESULT hr = o.GetSubscriberProperty(null, out v);
        hr = o.get_MachineName(out SysFreeStringSafeHandle sh);
        o.MachineName = bstr;
    }

#if NET5_0_OR_GREATER
    private static unsafe void IStream_GetsCCW()
    {
        IStream.Vtbl vtbl;
        IStream.PopulateVTable(&vtbl);
    }
#endif

#if NET7_0_OR_GREATER
    private static unsafe void GetVTable()
    {
        IUnknown.Vtbl* vtbl = GetVtable<IStream>();

        static IUnknown.Vtbl* GetVtable<T>()
            where T : IVTable
            => T.VTable;
    }
#endif

    private static unsafe void IUnknownGetsVtbl()
    {
        // WinForms needs the v-table to be declared for these base interfaces.
        IUnknown.Vtbl v;
        IDispatch.Vtbl v2;
    }
}
