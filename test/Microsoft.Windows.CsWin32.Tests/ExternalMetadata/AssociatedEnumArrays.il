// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Assemble with: ilasm /dll /output=AssociatedEnumArrays.winmd AssociatedEnumArrays.il

.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51) .ver 2:1:0:0 }
.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly Windows.Win32.winmd { .ver 1:0:0:0 }
.module AssociatedEnumArrays.winmd
.corflags 0x00000001

.class public sequential ansi sealed Windows.Win32.Foundation.ANCHOR
       extends [netstandard]System.ValueType
{
  .field public uint32 Value
}

.class public auto ansi sealed Windows.Win32.Test.ARRAY_MODE
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname uint32 value__
  .field public static literal valuetype Windows.Win32.Test.ARRAY_MODE First = uint32(1)
}

.class public sequential ansi sealed Windows.Win32.Test.ARRAY_RECORD
       extends [netstandard]System.ValueType
{
  .field public uint32[2] Modes
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute::.ctor(string) = (01 00 0A 41 52 52 41 59 5F 4D 4F 44 45 00 00)
}

.class public sequential ansi sealed Windows.Win32.Test.ARRAY_FLEX
       extends [netstandard]System.ValueType
{
  .field public uint32[1] Modes
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute::.ctor(string) = (01 00 0A 41 52 52 41 59 5F 4D 4F 44 45 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FlexibleArrayAttribute::.ctor() = (01 00 00 00)
}

.class public auto ansi sealed Windows.Win32.Test.SIGNED_MODE
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname uint32 value__
  .field public static literal valuetype Windows.Win32.Test.SIGNED_MODE First = uint32(1)
}

.class public sequential ansi sealed Windows.Win32.Test.SIGNED_ARRAY_RECORD
       extends [netstandard]System.ValueType
{
  .field public int32[2] Modes
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute::.ctor(string) = (01 00 0B 53 49 47 4E 45 44 5F 4D 4F 44 45 00 00)
}
