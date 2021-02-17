// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.Sdk
{
    using System;
    using System.Runtime.InteropServices;

    internal partial class PInvoke
    {
        /// <inheritdoc cref="CoCreateInstance(System.Guid*, IUnknown*, uint, System.Guid*, void**)"/>
        /// <seealso href="https://github.com/microsoft/CsWin32/issues/103" />
        internal static unsafe HRESULT CoCreateInstance<T>(in System.Guid rclsid, IUnknown* pUnkOuter, uint dwClsContext, in System.Guid riid, out T* ppv)
            where T : unmanaged
        {
            HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, riid, out void* o);
            ppv = (T*)o;
            return hr;
        }

        /// <inheritdoc cref="Marshal.ThrowExceptionForHR(int, IntPtr)" />
        /// <returns>The value from <paramref name="errorCode"/> if it does not reflect an error.</returns>
        /// <seealso cref="Marshal.ThrowExceptionForHR(int, IntPtr)"/>
        /// <seealso href="https://github.com/microsoft/CsWin32/issues/119" />
        internal static HRESULT ThrowOnFailure(this HRESULT errorCode, IntPtr errorInfo = default)
        {
            Marshal.ThrowExceptionForHR(errorCode, errorInfo);
            return errorCode;
        }
    }
}
