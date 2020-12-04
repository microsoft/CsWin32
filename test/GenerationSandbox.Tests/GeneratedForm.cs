// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Windows.Sdk;

#pragma warning disable CA1812 // dead code

/// <summary>
/// Contains "tests" that never run. Merely compiling is enough to verify the generated code has the right API shape.
/// </summary>
internal static unsafe class GeneratedForm
{
    private static void IEnumDebugPropertyInfo(IEnumDebugPropertyInfo* info)
    {
        Span<DebugPropertyInfo> span = stackalloc DebugPropertyInfo[2];
        uint initialized = 0;
        int result = info->Next(span, ref initialized); // should be _out_ instead of _ref_: https://github.com/microsoft/win32metadata/issues/38#issuecomment-738559618
        result = info->Clone(out IEnumDebugPropertyInfo* ppepi);
    }

    private static void IUnknown(IUnknown* pUnk)
    {
        int hr = pUnk->QueryInterface(Guid.NewGuid(), out void* ppvObject);
        uint c = pUnk->AddRef();
        uint r = pUnk->Release();
    }

    private static void IDispatch(IDispatch* pUnk)
    {
        int hr = pUnk->QueryInterface(Guid.NewGuid(), out void* ppvObject);
        uint c = pUnk->AddRef();
        uint r = pUnk->Release();
        int gti = pUnk->GetTypeInfo(0u, 0u, out ITypeInfo* ppTInfo);
    }
}
