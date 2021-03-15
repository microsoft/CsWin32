// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.Sdk
{
    using System;

    internal partial class PInvoke
    {
        /// <inheritdoc cref="CoCreateInstance(System.Guid*, object, uint, System.Guid*, void**)"/>
        /// <seealso href="https://github.com/microsoft/CsWin32/issues/103" />
        internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, object? pUnkOuter, uint dwClsContext, in Guid riid, out T ppv)
            where T : class
        {
            HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, riid, out T* o);
            ppv = (T)o;
            return hr;
        }
    }
}
