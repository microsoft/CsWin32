// Source for OcclusionSdk.winmd. Replaces baseline definitions with complete newer definitions.
// Regenerate with: ilasm OcclusionSdk.il /dll /output=OcclusionSdk.winmd

.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51) .ver 2:1:0:0 }
.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly extern OcclusionBaseline { .ver 1:0:0:0 }
.assembly OcclusionSdk { .ver 1:0:0:0 }
.module OcclusionSdk.winmd

.class public sequential ansi sealed Occlusion.Graphics.OUTER
       extends [netstandard]System.ValueType
{
  .class nested public sequential ansi sealed INNER extends [netstandard]System.ValueType
  {
    .field public int64 Value
  }
  .field public valuetype Occlusion.Graphics.OUTER/INNER Value
}

.class public sequential ansi sealed Occlusion.Graphics.SDK_BINDING
       extends [netstandard]System.ValueType
{
  .field public class [OcclusionBaseline]Occlusion.Graphics.IResource Buffer
  .field public valuetype [OcclusionBaseline]Occlusion.Graphics.OPTIONS Options
}

.class interface public abstract auto ansi Occlusion.Graphics.IResource
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32, uint16, uint16, uint8, uint8, uint8, uint8, uint8, uint8, uint8, uint8) = (01 00 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00)
  .method public hidebysig newslot abstract virtual instance void ExistingMethod() cil managed preservesig { }
  .method public hidebysig newslot abstract virtual instance void NewMethod() cil managed preservesig { }
}

.class interface public abstract auto ansi Occlusion.Graphics.IDerived
       implements [OcclusionBaseline]Occlusion.Graphics.IResource
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32, uint16, uint16, uint8, uint8, uint8, uint8, uint8, uint8, uint8, uint8) = (01 00 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00)
}

.class public auto ansi sealed Occlusion.Graphics.OPTIONS
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype Occlusion.Graphics.OPTIONS OldValue = int32(1)
  .field public static literal valuetype Occlusion.Graphics.OPTIONS NewValue = int32(2)
}

.class public sequential ansi sealed Occlusion.Graphics.ARCH_TYPE
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.SupportedArchitectureAttribute::.ctor(valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.Architecture) = (01 00 02 00 00 00 00 00)
  .field public int64 Value
}

.class public abstract sealed auto ansi Occlusion.Graphics.Apis
       extends [netstandard]System.Object
{
  .field public static literal int32 ReplacedConstant = int32(2)
  .method public static hidebysig pinvokeimpl("overlay.dll" winapi)
          int32 CreateResource(class Occlusion.Graphics.IResource resource) cil managed preservesig { }
}
