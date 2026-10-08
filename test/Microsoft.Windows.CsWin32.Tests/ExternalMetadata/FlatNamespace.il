// Source for FlatNamespace.winmd.
// Root and nested native typedefs and associated enums exercise custom marshaller namespaces.
// Regenerate with: ilasm FlatNamespace.il /dll /output=FlatNamespace.winmd

.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89 ) .ver 4:0:0:0 }
.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51 ) .ver 2:1:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly FlatNamespace { .ver 1:0:0:0 }
.module FlatNamespace.winmd
.module extern example.dll
.corflags 0x00000001

.class public sequential ansi sealed beforefieldinit FlatNamespace.TEST_HANDLE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = ( 01 00 00 00 )
  .field public void* Value
}

.class public auto ansi sealed FlatNamespace.TEST_ENUM
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype FlatNamespace.TEST_ENUM TestValue = int32(1)
}

.class public abstract auto autochar sealed beforefieldinit FlatNamespace.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("example.dll" nomangle winapi)
          void UseRootHandle([in] valuetype FlatNamespace.TEST_HANDLE handle) cil managed
  {
  }

  .method public hidebysig static pinvokeimpl("example.dll" nomangle winapi)
          void UseRootEnum([in] uint32 enumValue) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute::.ctor(string) = ( 01 00 09 54 45 53 54 5F 45 4E 55 4D 00 00 )
  }
}

.class public sequential ansi sealed beforefieldinit FlatNamespace.Nested.NESTED_HANDLE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = ( 01 00 00 00 )
  .field public void* Value
}

.class public auto ansi sealed FlatNamespace.Nested.NESTED_ENUM
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype FlatNamespace.Nested.NESTED_ENUM TestValue = int32(1)
}

.class public abstract auto autochar sealed beforefieldinit FlatNamespace.Nested.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("example.dll" nomangle winapi)
          void UseNestedHandle([in] valuetype FlatNamespace.Nested.NESTED_HANDLE handle) cil managed
  {
  }

  .method public hidebysig static pinvokeimpl("example.dll" nomangle winapi)
          void UseNestedEnum([in] uint32 enumValue) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute::.ctor(string) = ( 01 00 0B 4E 45 53 54 45 44 5F 45 4E 55 4D 00 00 )
  }
}
