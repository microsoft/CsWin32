// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Contextual ownership without type-level RAIIFree, including exact native types,
// incompatible cleanup, and cleanup requiring caller-supplied context.
// Assemble with: ilasm /dll /output=SafeHandleOwnership.winmd SafeHandleOwnership.il

.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51) .ver 2:1:0:0 }
.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly Windows.Win32 { .ver 1:0:0:0 }
.module SafeHandleOwnership.winmd
.module extern fixture.dll
.corflags 0x00000001

.class public sequential ansi sealed beforefieldinit Windows.Win32.Foundation.HANDLE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Other.HANDLE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Test.RESOURCE_A
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Test.RESOURCE_B
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Test.UNSIGNED_RESOURCE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public native unsigned int Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Test.UNSIGNED32_RESOURCE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public uint32 Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Test.DUPLICATE_RESOURCE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public sequential ansi sealed beforefieldinit Windows.Win32.Other.DUPLICATE_RESOURCE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = (01 00 00 00)
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = (01 00 00 00 00 00 00 00 00 00 00 00)
  .field public void* Value
}

.class public abstract auto autochar sealed beforefieldinit Windows.Win32.Test.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void FreeShared(void* resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void FreeOtherResource(valuetype Windows.Win32.Test.RESOURCE_B resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          bool FreeContext(valuetype Windows.Win32.Test.RESOURCE_A resource, int32 context) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void FreeUnsigned(valuetype Windows.Win32.Test.UNSIGNED_RESOURCE resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void FreeUnsigned32(valuetype Windows.Win32.Test.UNSIGNED32_RESOURCE resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void ConsumeResource([in] valuetype Windows.Win32.Test.RESOURCE_A resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void ConsumeHandle([in] valuetype Windows.Win32.Foundation.HANDLE resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void ConsumeOtherHandle([in] valuetype Windows.Win32.Other.HANDLE resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void ConsumePointer([in] void* resource) cil managed { }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void ConsumeGuid([in] valuetype [netstandard]System.Guid resource) cil managed { }
}

.class public abstract auto autochar sealed beforefieldinit Windows.Win32.Test.RAIIFree.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A CreateResource() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_B CreateOtherResource() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void* CreatePointer() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          int32 AcquirePointer([out] void** resource) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          int32 AcquireResource([out] valuetype Windows.Win32.Test.RESOURCE_A* resource) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A IncorrectCleanup() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 11 46 72 65 65 4F 74 68 65 72 52 65 73 6F 75 72 63 65 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A CreateWithContext() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0B 46 72 65 65 43 6F 6E 74 65 78 74 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.UNSIGNED_RESOURCE CreateUnsigned() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0C 46 72 65 65 55 6E 73 69 67 6E 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.UNSIGNED32_RESOURCE CreateUnsigned32() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0E 46 72 65 65 55 6E 73 69 67 6E 65 64 33 32 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.DUPLICATE_RESOURCE CreateDuplicate() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Other.DUPLICATE_RESOURCE CreateOtherDuplicate() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
}

.class public abstract auto autochar sealed beforefieldinit Windows.Win32.Test.FreeWith.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A CreateResource() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_B CreateOtherResource() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          void* CreatePointer() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          int32 AcquirePointer([out] void** resource) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          int32 AcquireResource([out] valuetype Windows.Win32.Test.RESOURCE_A* resource) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A IncorrectCleanup() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 11 46 72 65 65 4F 74 68 65 72 52 65 73 6F 75 72 63 65 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.RESOURCE_A CreateWithContext() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0B 46 72 65 65 43 6F 6E 74 65 78 74 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.UNSIGNED_RESOURCE CreateUnsigned() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0C 46 72 65 65 55 6E 73 69 67 6E 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.UNSIGNED32_RESOURCE CreateUnsigned32() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0E 46 72 65 65 55 6E 73 69 67 6E 65 64 33 32 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Test.DUPLICATE_RESOURCE CreateDuplicate() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
  .method public hidebysig static pinvokeimpl("fixture.dll" nomangle winapi)
          valuetype Windows.Win32.Other.DUPLICATE_RESOURCE CreateOtherDuplicate() cil managed
  {
    .param [0]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.FreeWithAttribute::.ctor(string) = (01 00 0A 46 72 65 65 53 68 61 72 65 64 00 00)
  }
}
