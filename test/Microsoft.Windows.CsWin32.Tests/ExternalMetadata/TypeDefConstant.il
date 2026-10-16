// Source for TypeDefConstant.winmd.
// Fixture verifying that constants typed as typedef structs defined in the same
// metadata module (encoded as TypeDefinition handles rather than TypeReference handles)
// correctly nest into the struct declaration and attach to extension blocks.

.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51 ) .ver 2:1:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly TypeDefConstant { .ver 1:0:0:0 }
.module TypeDefConstant.winmd
.module extern somedll.dll
.corflags 0x00000001

.class public sequential ansi sealed beforefieldinit TypeDefConstant.Foundation.CUSTOM_HANDLE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = ( 01 00 00 00 )
  .field public native int Value
}

.class public abstract auto autochar sealed beforefieldinit TypeDefConstant.Apis
       extends [netstandard]System.Object
{
  .field public static initonly valuetype TypeDefConstant.Foundation.CUSTOM_HANDLE CUSTOM_HANDLE_VALUE = int32(1)
}
