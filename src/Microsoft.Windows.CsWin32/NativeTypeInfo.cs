// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace Microsoft.Windows.CsWin32
{
    internal readonly struct NativeTypeInfo
    {
        public NativeTypeInfo(UnmanagedType? unmanagedType, bool isNullTerminated, short? sizeParamIndex, int? sizeConst)
        {
            this.UnmanagedType = unmanagedType;
            this.IsNullTerminated = isNullTerminated;
            this.SizeParamIndex = sizeParamIndex;
            this.SizeConst = sizeConst;
        }

        public UnmanagedType? UnmanagedType { get; }

        public bool IsNullTerminated { get; }

        public short? SizeParamIndex { get; }

        public int? SizeConst { get; }
    }
}
