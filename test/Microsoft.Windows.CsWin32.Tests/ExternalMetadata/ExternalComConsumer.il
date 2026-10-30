// Source for ExternalComConsumer.winmd. References ExternalComSdk reciprocally.
// Regenerate with: ilasm ExternalComConsumer.il /dll /output=ExternalComConsumer.winmd

.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern netstandard { .publickeytoken = (CC 7B 13 FF CD 2D DD 51) .ver 2:1:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly extern ExternalComSdk { .ver 1:0:0:0 }
.assembly ExternalComConsumer { .ver 1:0:0:0 }
.module ExternalComConsumer.winmd

.class public sequential ansi sealed beforefieldinit ExternalCom.BINDING
       extends [netstandard]System.ValueType
{
  .field public class [ExternalComSdk]ExternalCom.IExternalCom ExternalCom
  .field public class ExternalCom.ILocalCom LocalCom
  .field public class [ExternalComSdk]ExternalCom.IExternalCom* ExternalComArray
  .field public class ExternalCom.ILocalCom* LocalComArray
}

.class public sequential ansi sealed beforefieldinit ExternalCom.NATIVE_BINDING
       extends [netstandard]System.ValueType
{
  .field public class [ExternalComSdk]ExternalCom.IExternalNative ExternalNative
  .field public class ExternalCom.ILocalNative LocalNative
}

.class interface public abstract auto ansi ExternalCom.IBaseCom
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .method public hidebysig newslot abstract virtual instance void BaseMethod() cil managed preservesig { }
}

.class interface public abstract auto ansi ExternalCom.IBaseNative
{
  .method public hidebysig newslot abstract virtual instance void NativeMethod() cil managed preservesig { }
}

.class interface public abstract auto ansi ExternalCom.ILocalCom
       implements [ExternalComSdk]ExternalCom.IExternalCom
{
}

.class interface public abstract auto ansi ExternalCom.ILocalNative
       implements [ExternalComSdk]ExternalCom.IExternalNative
{
}
