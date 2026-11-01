// Source for ExternalComSdk.winmd. Base interfaces belong to ExternalComConsumer.
// Regenerate with: ilasm ExternalComSdk.il /dll /output=ExternalComSdk.winmd

.assembly extern ExternalComConsumer { .ver 1:0:0:0 }
.assembly extern Windows.Win32.winmd { .ver 0:0:0:0 }
.assembly ExternalComSdk { .ver 1:0:0:0 }
.module ExternalComSdk.winmd

.class interface public abstract auto ansi ExternalCom.Sdk.IExternalCom
       implements [ExternalComConsumer]ExternalCom.IBaseCom
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32, uint16, uint16, uint8, uint8, uint8, uint8, uint8, uint8, uint8, uint8) = (01 00 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 02 00 00)
}

.class interface public abstract auto ansi ExternalCom.Sdk.IExternalNative
       implements [ExternalComConsumer]ExternalCom.IBaseNative
{
}
