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
    private static void IEnumDebugPropertyInfo(IEnumDebugPropertyInfo info)
    {
        Span<DebugPropertyInfo> span = new DebugPropertyInfo[2];
        HRESULT result = info.Next(span, out uint initialized);
        result = info.Clone(out IEnumDebugPropertyInfo ppepi);
    }
}
