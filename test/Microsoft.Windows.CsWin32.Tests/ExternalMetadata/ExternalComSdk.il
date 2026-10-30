// Source for ExternalComSdk.winmd. Base interfaces belong to ExternalComConsumer.
// Regenerate with: ilasm ExternalComSdk.il /dll /output=ExternalComSdk.winmd

.assembly extern ExternalComConsumer { .ver 1:0:0:0 }
.assembly ExternalComSdk { .ver 1:0:0:0 }
.module ExternalComSdk.winmd

.class interface public abstract auto ansi ExternalCom.IExternalCom
       implements [ExternalComConsumer]ExternalCom.IBaseCom
{
}

.class interface public abstract auto ansi ExternalCom.IExternalNative
       implements [ExternalComConsumer]ExternalCom.IBaseNative
{
}
