// Source for OcclusionBaseline.winmd.
// Regenerate with: ilasm OcclusionBaseline.il /dll /output=OcclusionBaseline.winmd

.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51) .ver 2:1:0:0 }
.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly OcclusionBaseline { .ver 1:0:0:0 }
.module OcclusionBaseline.winmd

.class public sequential ansi sealed Occlusion.Binding.BINDING
       extends [netstandard]System.ValueType
{
  .field public class Occlusion.Graphics.IResource Buffer
  .field public valuetype Occlusion.Graphics.OPTIONS Options
  .field public valuetype Occlusion.Graphics.OUTER/INNER Nested
  .field public valuetype Occlusion.Graphics.ARCH_TYPE Architecture
}

.class public sequential ansi sealed Occlusion.Graphics.OUTER
       extends [netstandard]System.ValueType
{
  .class nested public sequential ansi sealed INNER extends [netstandard]System.ValueType
  {
    .field public int32 Value
  }
  .field public valuetype Occlusion.Graphics.OUTER/INNER Value
}

.class interface public abstract auto ansi Occlusion.Graphics.IResource
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32, uint16, uint16, uint8, uint8, uint8, uint8, uint8, uint8, uint8, uint8) = (01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00)
  .method public hidebysig newslot abstract virtual instance void ExistingMethod() cil managed preservesig { }
}

.class public auto ansi sealed Occlusion.Graphics.OPTIONS
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype Occlusion.Graphics.OPTIONS OldValue = int32(1)
}

.class public sequential ansi sealed Occlusion.Graphics.ARCH_TYPE
       extends [netstandard]System.ValueType
{
  .field public int32 Value
}

.class public abstract sealed auto ansi Occlusion.Graphics.Apis
       extends [netstandard]System.Object
{
  .field public static literal int32 UnrelatedConstant = int32(1)
  .field public static literal int32 ReplacedConstant = int32(1)
  .method public static hidebysig pinvokeimpl("overlay.dll" winapi)
          void CreateResource(class Occlusion.Graphics.IResource resource) cil managed preservesig { }
  .method public static hidebysig pinvokeimpl("overlay.dll" winapi)
          void UnrelatedApi() cil managed preservesig { }
}
