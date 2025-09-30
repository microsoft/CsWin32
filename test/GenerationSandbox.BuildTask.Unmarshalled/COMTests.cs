// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134, SA1307, SA1308

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
//using Windows.Win32.System.Com;


//[CustomMarshaller(typeof(T), MarshalMode.ManagedToUnmanagedIn, typeof(WinRTMarshaler<T>))]
//[CustomMarshaller(typeof(T), MarshalMode.ManagedToUnmanagedOut, typeof(WinRTMarshaler<T>))]
//[CustomMarshaller(typeof(T), MarshalMode.UnmanagedToManagedIn, typeof(WinRTMarshaler<T>))]
//[CustomMarshaller(typeof(T), MarshalMode.UnmanagedToManagedOut, typeof(WinRTMarshaler<T>))]
//internal static class WinRTMarshaler<T>
//{
//    public static unsafe T ConvertToManaged(nint unmanaged)
//    {
//        return WinRT.MarshalInterface<T>.FromAbi(unmanaged);
//    }

//    public static nint ConvertToUnmanaged(T managed)
//    {
//        return WinRT.MarshalInterface<T>.FromManaged(managed);
//    }

//    public static void Free(nint unmanaged)
//    {
//        WinRT.MarshalInterface<T>.DisposeAbi(unmanaged);
//    }
//}

//[CustomMarshaller(typeof(DispatcherQueueController), MarshalMode.ManagedToUnmanagedIn, typeof(WinRTMarshalerDispatcherQueueController))]
//[CustomMarshaller(typeof(DispatcherQueueController), MarshalMode.ManagedToUnmanagedOut, typeof(WinRTMarshalerDispatcherQueueController))]
//[CustomMarshaller(typeof(DispatcherQueueController), MarshalMode.UnmanagedToManagedIn, typeof(WinRTMarshalerDispatcherQueueController))]
//[CustomMarshaller(typeof(DispatcherQueueController), MarshalMode.UnmanagedToManagedOut, typeof(WinRTMarshalerDispatcherQueueController))]
//internal static class WinRTMarshalerDispatcherQueueController
//{
//    public static unsafe DispatcherQueueController ConvertToManaged(nint unmanaged)
//    {
//        return WinRT.MarshalInterface<DispatcherQueueController>.FromAbi(unmanaged);
//    }

//    public static nint ConvertToUnmanaged(DispatcherQueueController managed)
//    {
//        return WinRT.MarshalInterface<DispatcherQueueController>.FromManaged(managed);
//    }

//    public static void Free(nint unmanaged)
//    {
//        WinRT.MarshalInterface<DispatcherQueueController>.DisposeAbi(unmanaged);
//    }
//}


//[Guid("0000000C-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface()]
//[SupportedOSPlatform("windows5.0")]
//[global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.235+132ca681b2.D")]
//internal partial interface IStream2
//    : Windows.Win32.System.Com.ISequentialStream
//{
//    [PreserveSig()]
//    unsafe new Windows.Win32.Foundation.HRESULT Read(void* pv, uint cb, [Optional] uint* pcbRead);

//    [PreserveSig()]
//    unsafe new Windows.Win32.Foundation.HRESULT Write(void* pv, uint cb, [Optional] uint* pcbWritten);

//    unsafe void Seek(long dlibMove, global::System.IO.SeekOrigin dwOrigin, [Optional] ulong* plibNewPosition);

//    void SetSize(ulong libNewSize);

//    unsafe void CopyTo(IStream2 pstm, ulong cb, [Optional] ulong* pcbRead, [Optional] ulong* pcbWritten);

//    void Commit([MarshalUsing(typeof(STGCToUintMarshaller))] Windows.Win32.System.Com.STGC grfCommitFlags);

//    void Revert();

//    void LockRegion(ulong libOffset, ulong cb, [MarshalUsing(typeof(EnumToUintMarshaller<Windows.Win32.System.Com.LOCKTYPE>))] Windows.Win32.System.Com.LOCKTYPE dwLockType);

//    void UnlockRegion(ulong libOffset, ulong cb, uint dwLockType);

//    unsafe void Stat(Windows.Win32.System.Com.STATSTG* pstatstg, [MarshalUsing(typeof(EnumToUintMarshaller<Windows.Win32.System.Com.STATFLAG>))] Windows.Win32.System.Com.STATFLAG grfStatFlag);

//    void Clone(out IStream2 ppstm);
//}

//[CustomMarshaller(typeof(STGC), MarshalMode.ManagedToUnmanagedIn, typeof(STGCToUintMarshaller))]
//[CustomMarshaller(typeof(STGC), MarshalMode.ManagedToUnmanagedOut, typeof(STGCToUintMarshaller))]
//[CustomMarshaller(typeof(STGC), MarshalMode.UnmanagedToManagedIn, typeof(STGCToUintMarshaller))]
//[CustomMarshaller(typeof(STGC), MarshalMode.UnmanagedToManagedOut, typeof(STGCToUintMarshaller))]
//[CustomMarshaller(typeof(STGC), MarshalMode.ElementIn, typeof(STGCToUintMarshaller))]
//[CustomMarshaller(typeof(STGC), MarshalMode.ElementOut, typeof(STGCToUintMarshaller))]
//internal static class STGCToUintMarshaller
//{
//    public static unsafe STGC ConvertToManaged(uint unmanaged)
//    {
//        return (STGC)unmanaged;
//    }

//    public static uint ConvertToUnmanaged(STGC managed)
//    {
//        return (uint)managed;
//    }

//    public static void Free(uint unmanaged)
//    {
//    }
//}

namespace Windows.Win32.System.Diagnostics.Debug
{
    //[NativeMarshalling(typeof(DebugPropertyInfoMarshaller))]
    internal partial struct DebugPropertyInfo
    {
        //internal unsafe struct __Native
        //{
        //    internal uint m_dwValidFields;

        //    internal BSTR m_bstrName;

        //    internal BSTR m_bstrType;

        //    internal BSTR m_bstrValue;

        //    internal BSTR m_bstrFullName;

        //    internal uint m_dwAttrib;

        //    internal void* m_pDebugProp;
        //}

        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.ManagedToUnmanagedIn, typeof(DebugPropertyInfoMarshaller))]
        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.ManagedToUnmanagedOut, typeof(DebugPropertyInfoMarshaller))]
        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.UnmanagedToManagedIn, typeof(DebugPropertyInfoMarshaller))]
        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.UnmanagedToManagedOut, typeof(DebugPropertyInfoMarshaller))]
        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.ElementIn, typeof(DebugPropertyInfoMarshaller))]
        //[CustomMarshaller(typeof(DebugPropertyInfo), MarshalMode.ElementOut, typeof(DebugPropertyInfoMarshaller))]
        //internal static unsafe class DebugPropertyInfoMarshaller
        //{
        //    public static DebugPropertyInfo ConvertToManaged(DebugPropertyInfo.__Native unmanaged)
        //    {
        //        try
        //        {
        //            DebugPropertyInfo managed = new()
        //            {
        //                m_dwValidFields = unmanaged.m_dwValidFields,
        //                m_bstrName = unmanaged.m_bstrName,
        //                m_bstrType = unmanaged.m_bstrType,
        //                m_bstrValue = unmanaged.m_bstrValue,
        //                m_bstrFullName = unmanaged.m_bstrFullName,
        //                m_dwAttrib = unmanaged.m_dwAttrib,
        //                m_pDebugProp = ComInterfaceMarshaller<IDebugProperty>.ConvertToManaged(unmanaged.m_pDebugProp),
        //            };

        //            return managed;
        //        }
        //        finally
        //        {
        //            ComInterfaceMarshaller<IDebugProperty>.Free(unmanaged.m_pDebugProp);
        //        }
        //    }

        //    public static DebugPropertyInfo.__Native ConvertToUnmanaged(DebugPropertyInfo managed)
        //    {
        //        DebugPropertyInfo.__Native unmanaged = new()
        //        {
        //            m_dwValidFields = managed.m_dwValidFields,
        //            m_bstrName = managed.m_bstrName,
        //            m_bstrType = managed.m_bstrType,
        //            m_bstrValue = managed.m_bstrValue,
        //            m_bstrFullName = managed.m_bstrFullName,
        //            m_dwAttrib = managed.m_dwAttrib,
        //            m_pDebugProp = ComInterfaceMarshaller<IDebugProperty>.ConvertToUnmanaged(managed.m_pDebugProp),
        //        };
        //        return unmanaged;
        //    }

        //    public static void Free(DebugPropertyInfo.__Native unmanaged)
        //    {
        //        ComInterfaceMarshaller<IDebugProperty>.Free(unmanaged.m_pDebugProp);
        //    }
        //}
    }
}

public partial class COMTests
{
    [Fact]
    public void Placeholder()
    {
    }

    [LibraryImport("query.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial void LoadIFilter(PCWSTR pwcsPath, Windows.Win32.System.Diagnostics.Debug.IDebugProperty pUnkOuter, out Windows.Win32.System.Diagnostics.Debug.IDebugProperty ppIUnk);


    //[LibraryImport("CoreMessaging.dll")]
    //internal static partial HRESULT CreateDispatcherQueueController(int options,
    //    [MarshalUsing(typeof(WinRTMarshaler<DispatcherQueueController>))] out DispatcherQueueController dispatcherQueueController);

    ////[LibraryImport("query.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    ////internal static unsafe partial void LoadIFilter2([MarshalUsing(typeof(SpanMarshaller<int, int>))] Span<int> x);

    //[LibraryImport("bcrypt.dll")]
    //internal static partial int BCryptFinishHash(int hHash, Span<byte> pbOutput, int cbOutput, int dwFlags);
    ////internal static unsafe partial HRESULT LoadIFilter(PCWSTR pwcsPath, IUnknown pUnkOuter, void** ppIUnk);

    //[GeneratedComInterface]
    //[Guid("00000000-0000-0000-C000-000000000046")]
    //internal partial interface IUnknown
    //{
    //}
}

/*
namespace Com
{
    [Guid("0C733A30-2A1C-11CE-ADE5-00AA0044773D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface()]
    [SupportedOSPlatform("windows5.0")]
    [global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.220+3d660429f5.D")]
    internal partial interface ISequentialStream
    {
        unsafe void Read(void* pv, uint cb, [Optional] uint* pcbRead);

        unsafe void Write(void* pv, uint cb, [Optional] uint* pcbWritten);
    }

    internal static unsafe class STGCMarshaller
    {
        public static uint ConvertToUnmanaged(STGC managed)
            => (uint)managed;

        public static STGC ConvertToManaged(uint unmanaged)
            => (STGC)unmanaged;

        public static void Free(uint* unmanaged)
            => throw new NotImplementedException();
    }

    internal static unsafe class STATFLAGMarshaller
    {
        public static uint ConvertToUnmanaged(STATFLAG managed)
            => (uint)managed;

        public static STATFLAG ConvertToManaged(uint unmanaged)
            => (STATFLAG)unmanaged;

        public static void Free(uint* unmanaged)
            => throw new NotImplementedException();
    }


    internal static unsafe class LOCKTYPEMarshaller
    {
        public static uint ConvertToUnmanaged(LOCKTYPE managed)
            => (uint)managed;

        public static LOCKTYPE ConvertToManaged(uint unmanaged)
            => (LOCKTYPE)unmanaged;

        public static void Free(uint* unmanaged)
            => throw new NotImplementedException();
    }

    [Guid("0000000C-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), GeneratedComInterface()]
    [SupportedOSPlatform("windows5.0")]
    [global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.220+3d660429f5.D")]
    internal partial interface IStream : ISequentialStream
    {
        unsafe new void Read(void* pv, uint cb, [Optional] uint* pcbRead);

        unsafe new void Write(void* pv, uint cb, [Optional] uint* pcbWritten);

        unsafe void Seek(long dlibMove, global::System.IO.SeekOrigin dwOrigin, [Optional] ulong* plibNewPosition);

        void SetSize(ulong libNewSize);

        unsafe void CopyTo(IStream pstm, ulong cb, [Optional] ulong* pcbRead, [Optional] ulong* pcbWritten);

        //void Commit([MarshalUsing(typeof(STGCMarshaller))] STGC grfCommitFlags);
        void Commit(STGC grfCommitFlags);


        void Revert();

        void LockRegion(ulong libOffset, ulong cb, LOCKTYPE dwLockType);

        void UnlockRegion(ulong libOffset, ulong cb, uint dwLockType);

        unsafe void Stat(STATSTG* pstatstg, STATFLAG grfStatFlag);

        void Clone(out IStream ppstm);
    }

    // TODO: IDispatch

    [Guid("4A6B0E15-2E38-11D1-9965-00C04FBBB345"), GeneratedComInterface()]
    [SupportedOSPlatform("windows5.0")]
    [global::System.CodeDom.Compiler.GeneratedCode("Microsoft.Windows.CsWin32", "0.3.220+3d660429f5.D")]
    internal partial interface IEventSubscription
    {
        [return: MarshalAs(UnmanagedType.BStr)] string get_SubscriptionID();

        void put_SubscriptionID([MarshalAs(UnmanagedType.BStr)] string SubscriptionID);
    }
}
*/
