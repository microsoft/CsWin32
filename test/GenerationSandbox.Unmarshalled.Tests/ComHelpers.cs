// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET7_0_OR_GREATER

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Windows.Win32;

// The `unsafe` modifier is only allowed to appear on the class declaration -- not the partial method declaration.
// See https://github.com/dotnet/csharplang/discussions/7298 for more.
internal unsafe partial class ComHelpers
{
    static partial void PopulateIUnknownImpl<TComInterface>(IUnknown.Vtbl* vtable)
        where TComInterface : unmanaged
    {
        // IUnknown member initialization of the v-table would go here.
        vtable->QueryInterface_1 = TestComWrappers.ComWrappersForIUnknown.QueryInterface_1;
        vtable->AddRef_2 = TestComWrappers.ComWrappersForIUnknown.AddRef_2;
        vtable->Release_3 = TestComWrappers.ComWrappersForIUnknown.Release_3;
    }

    private unsafe class TestComWrappers : ComWrappers
    {
        internal static readonly IUnknown.Vtbl ComWrappersForIUnknown = GetComWrappersUnknown();

        // Abstracts that need implementation
        protected override unsafe ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = 0;
            return null;
        }

        protected override object? CreateObject(nint externalComObject, CreateObjectFlags flags) => null;

        protected override void ReleaseObjects(global::System.Collections.IEnumerable objects) => throw new NotImplementedException();

        private static IUnknown.Vtbl GetComWrappersUnknown()
        {
            GetIUnknownImpl(out IntPtr fpQueryInterface, out IntPtr fpAddRef, out IntPtr fpRelease);
            return new IUnknown.Vtbl()
            {
                QueryInterface_1 = (delegate* unmanaged[Stdcall]<IUnknown*, Guid*, void**, HRESULT>)fpQueryInterface,
                AddRef_2 = (delegate* unmanaged[Stdcall]<IUnknown*, uint>)fpAddRef,
                Release_3 = (delegate* unmanaged[Stdcall]<IUnknown*, uint>)fpRelease,
            };
        }
    }
}

#endif
