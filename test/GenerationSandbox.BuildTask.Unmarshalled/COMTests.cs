// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable IDE0005
#pragma warning disable SA1201, SA1512, SA1005, SA1507, SA1515, SA1403, SA1402, SA1411, SA1300, SA1313, SA1134

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
//using Windows.Win32.System.Com;


public partial class COMTests
{
    [Fact]
    public void Placeholder()
    {
    }

    //[LibraryImport("query.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    //internal static unsafe partial void LoadIFilter(PCWSTR pwcsPath, Com.ISequentialStream pUnkOuter, out Com.ISequentialStream ppIUnk);

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
