// Source for WnfWithoutStatusSuccess.winmd.
// This intentionally keeps only the minimal metadata required to reproduce
// issue #1677: one WNF API, its NTSTATUS-based release method, and the
// handle typedef that points to that release method.

.module extern ntdll.dll

.assembly extern Windows.Win32.winmd
{
  .ver 0:0:0:0
}

.assembly extern netstandard
{
  .publickeytoken = (CC 7B 13 FF CD 2D DD 51 )
  .ver 2:1:0:0
}

.assembly WnfWithoutStatusSuccess
{
  .ver 1:0:0:0
}

.module WnfWithoutStatusSuccess.winmd
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003
.corflags 0x00000001

.class public abstract auto autochar sealed beforefieldinit OSClient.Repro.NamespaceAnchor
       extends [netstandard]System.Object
{
} // end of class OSClient.Repro.NamespaceAnchor

.class public abstract auto autochar sealed beforefieldinit OSClient.WNF.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi)
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS
          RtlSubscribeWnfStateChangeNotification([out] valuetype OSClient.WNF.PWNF_USER_SUBSCRIPTION* Subscription) cil managed
  {
  }

  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi)
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS
          RtlUnsubscribeWnfStateChangeNotification([in] valuetype OSClient.WNF.PWNF_USER_SUBSCRIPTION Subscription) cil managed
  {
  }
} // end of class OSClient.WNF.Apis

.class public sequential ansi sealed beforefieldinit OSClient.WNF.PWNF_USER_SUBSCRIPTION
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = ( 01 00 28 52 74 6C 55 6E 73 75 62 73 63 72 69 62   // ..(RtlUnsubscrib
                                                                                               65 57 6E 66 53 74 61 74 65 43 68 61 6E 67 65 4E   // eWnfStateChangeN
                                                                                               6F 74 69 66 69 63 61 74 69 6F 6E 00 00 )          // otification..
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = ( 01 00 00 00 00 00 00 00 00 00 00 00 )
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = ( 01 00 00 00 )
  .field public void* Value
} // end of class OSClient.WNF.PWNF_USER_SUBSCRIPTION
