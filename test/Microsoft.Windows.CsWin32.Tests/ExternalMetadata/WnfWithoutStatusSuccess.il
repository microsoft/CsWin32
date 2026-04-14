// Source for WnfWithoutStatusSuccess.winmd.
// This fixture exists to reproduce issue #1677 without depending on the full
// WinmdGenerator pipeline for a tiny regression asset.

//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0
//  Copyright (c) Microsoft Corporation.  All rights reserved.



// Metadata version: v4.0.30319
.module extern ntdll.dll
.module extern kernelbase.dll
.module extern combase.dll
.module extern sechost.dll
.assembly extern Windows.Win32.winmd
{
  .ver 0:0:0:0
}
.assembly extern netstandard
{
  .publickeytoken = (CC 7B 13 FF CD 2D DD 51 )                         // .{...-.Q
  .ver 2:1:0:0
}
.assembly OSClientCsWin32Metadata.winmd
{
  .ver 255:255:255:255
}
.module OSClientCsWin32Metadata.winmd
// MVID: {F6AF2548-127A-4CD1-BA8A-367789CD92AC}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x0000014F39B60000


// =============== CLASS MEMBERS DECLARATION ===================

.class public abstract auto autochar sealed beforefieldinit OSClient.WNF.Apis
       extends [netstandard]System.Object
{
  .field public static literal uint32 MAXULONG = uint32(0xFFFFFFFF)
  .field public static literal valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS STATUS_BUFFER_TOO_SMALL = int32(0xC0000023)
  .field public static literal uint32 WNF_NORMAL_DELIVERY = uint32(0x00000001)
  .field public static literal uint32 WNF_RELIABLE_DELIVERY = uint32(0x00000004)
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          RtlPublishWnfStateData([in] valuetype OSClient.WNF.WNF_STATE_NAME StateName,
                                 [in][opt] valuetype OSClient.WNF._WNF_TYPE_ID* TypeId,
                                 [in][opt] void* StateData,
                                 [in] uint32 StateDataLength,
                                 [in][opt] void* ExplicitScope) cil managed
  {
    .param [3]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.MemorySizeAttribute::.ctor() = ( 01 00 01 00 53 06 0F 42 79 74 65 73 50 61 72 61   // ....S..BytesPara
                                                                                             6D 49 6E 64 65 78 03 00 )                         // mIndex..
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          RtlTestAndPublishWnfStateData([in] valuetype OSClient.WNF.WNF_STATE_NAME StateName,
                                        [in][opt] valuetype OSClient.WNF._WNF_TYPE_ID* TypeId,
                                        [in][opt] void* StateData,
                                        [in] uint32 StateDataLength,
                                        [in][opt] void* ExplicitScope,
                                        [in] uint32 MatchingChangeStamp) cil managed
  {
    .param [3]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.MemorySizeAttribute::.ctor() = ( 01 00 01 00 53 06 0F 42 79 74 65 73 50 61 72 61   // ....S..BytesPara
                                                                                             6D 49 6E 64 65 78 03 00 )                         // mIndex..
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          RtlSubscribeWnfStateChangeNotification([out] valuetype OSClient.WNF.PWNF_USER_SUBSCRIPTION* Subscription,
                                                 [in] valuetype OSClient.WNF.WNF_STATE_NAME StateName,
                                                 [in] uint32 ChangeStamp,
                                                 [in] class OSClient.WNF.PWNF_USER_CALLBACK Callback,
                                                 [in][opt] void* CallbackContext,
                                                 [in][opt] valuetype OSClient.WNF._WNF_TYPE_ID* TypeId,
                                                 [in][opt] uint32 SerializationGroup,
                                                 [in][opt] uint32 DeliveryOptions) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          NtQueryWnfStateData([in] valuetype OSClient.WNF.WNF_STATE_NAME* StateName,
                              [in][opt] valuetype OSClient.WNF._WNF_TYPE_ID* TypeId,
                              [in][opt] void* ExplicitScope,
                              [out] uint32* ChangeStamp,
                              [out][opt] void* Buffer,
                              [in][out] uint32* BufferSize) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .param [2]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .param [3]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .param [5]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.MemorySizeAttribute::.ctor() = ( 01 00 01 00 53 06 0F 42 79 74 65 73 50 61 72 61   // ....S..BytesPara
                                                                                             6D 49 6E 64 65 78 05 00 )                         // mIndex..
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          RtlUnsubscribeWnfNotificationWaitForCompletion([in] valuetype OSClient.WNF.PWNF_USER_SUBSCRIPTION Subscription) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          RtlUnsubscribeWnfStateChangeNotification([in] valuetype OSClient.WNF.PWNF_USER_SUBSCRIPTION Subscription) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          NtCreateWnfStateName([out] valuetype OSClient.WNF.WNF_STATE_NAME* StateName,
                               [in] uint32 NameLifeTime,
                               [in] uint32 DataScope,
                               [in] uint32 PersistData,
                               [in][opt] valuetype OSClient.WNF._WNF_TYPE_ID* TypeId,
                               [in] uint32 MaximumStateSize,
                               [in] void* SecurityDescriptor) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("ntdll.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          NtDeleteWnfStateName([in] valuetype OSClient.WNF.WNF_STATE_NAME* StateName) cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
  }
} // end of class OSClient.WNF.Apis

.class public sequential ansi sealed beforefieldinit OSClient.FeatureTuning.__FC_CONFIGCHANGE_SUBSCRIPTION
       extends [netstandard]System.ValueType
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.RAIIFreeAttribute::.ctor(string) = ( 01 00 10 5F 5F 46 43 5F 55 6E 73 75 62 73 63 72   // ...__FC_Unsubscr
                                                                                               69 62 65 00 00 )                                  // ibe..
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = ( 01 00 FF FF FF FF FF FF FF FF 00 00 ) 
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.InvalidHandleValueAttribute::.ctor(int64) = ( 01 00 00 00 00 00 00 00 00 00 00 00 ) 
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.NativeTypedefAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public void* Value
} // end of class OSClient.FeatureTuning.__FC_CONFIGCHANGE_SUBSCRIPTION

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

.class public auto ansi sealed OSClient.FeatureTuning.__FC_System_Flags
       extends [netstandard]System.Enum
{
  .custom instance void [netstandard]System.FlagsAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype OSClient.FeatureTuning.__FC_System_Flags __FC_System_Flags_none = int32(0x00000000)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_System_Flags __FC_System_Flags_suppress_config = int32(0x00000001)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_System_Flags __FC_System_Flags_report_schema = int32(0x00000100)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_System_Flags __FC_System_Flags_report_config = int32(0x00000200)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_System_Flags __FC_System_Flags_report_full_config = int32(0x00000400)
} // end of class OSClient.FeatureTuning.__FC_System_Flags

.class public auto ansi sealed OSClient.FeatureTuning.__FC_Report_Kind
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_none = int32(0x00000000)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_schema = int32(0x00000064)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_config = int32(0x0000006E)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_full_config = int32(0x0000006F)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_impression = int32(0x00000078)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_explicit_impression = int32(0x00000079)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_errors_base = int32(0x000003E8)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_property_not_found = int32(0x000003E9)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_property_write_value_failed = int32(0x000003EA)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_property_validate_failed = int32(0x000003EB)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_object_not_found = int32(0x000003EC)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_object_validate_failed = int32(0x000003ED)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_array_not_found = int32(0x000003EE)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_array_validate_failed = int32(0x000003EF)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_object_version_missing = int32(0x000003F0)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_object_version_mismatch = int32(0x000003F1)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_payload_error_base = int32(0x000003E9)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_payload_error_last = int32(0x000003F1)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_mismatched_id = int32(0x0000044D)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_mismatched_change_time = int32(0x0000044E)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_unrecognized_root_object = int32(0x0000044F)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_structure_too_deep = int32(0x00000450)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_structure_error_base = int32(0x0000044D)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_structure_error_last = int32(0x00000450)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error = int32(0x00000898)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_property_within_array = int32(0x00000899)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_unnamed_object_without_array = int32(0x0000089A)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_named_object_in_array = int32(0x0000089B)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_expected_object = int32(0x0000089C)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_unexpected_object_finish = int32(0x0000089D)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_nested_array = int32(0x0000089F)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_array_without_name = int32(0x000008A0)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_expected_array = int32(0x000008A1)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_unexpected_array_finish = int32(0x000008A2)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_unexpected_finish = int32(0x000008A3)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_base = int32(0x00000898)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_parse_error_last = int32(0x000008A3)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error = int32(0x00000960)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_cached_tson_duplicate = int32(0x00000961)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_cannot_create_process_marker = int32(0x00000962)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_storage_load_config_failure = int32(0x00000963)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_tson_duplicate_to_cache = int32(0x00000964)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_serialization_failure = int32(0x00000965)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_base = int32(0x00000960)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Report_Kind __FC_Report_Kind_system_error_last = int32(0x00000965)
} // end of class OSClient.FeatureTuning.__FC_Report_Kind

.class public auto ansi sealed OSClient.FeatureTuning.__FC_Query_Flags
       extends [netstandard]System.Enum
{
  .custom instance void [netstandard]System.FlagsAttribute::.ctor() = ( 01 00 00 00 ) 
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_none = int32(0x00000000)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_data = int32(0x00000001)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_sequence = int32(0x00000002)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_refresh = int32(0x00000008)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_first = int32(0x01000000)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_reboot = int32(0x02000000)
  .field public static literal valuetype OSClient.FeatureTuning.__FC_Query_Flags __FC_Query_Flags_lastprocess = int32(0x04000000)
} // end of class OSClient.FeatureTuning.__FC_Query_Flags

.class public sequential ansi sealed beforefieldinit OSClient.FeatureTuning.__FC_Config_Info
       extends [netstandard]System.ValueType
{
  .field public uint32 sequence
  .field public valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.PSTR data
  .field public valuetype OSClient.FeatureTuning.__FC_Report_Kind report
} // end of class OSClient.FeatureTuning.__FC_Config_Info

.class public auto autochar sealed beforefieldinit OSClient.FeatureTuning.__PFC_CONFIGCHANGE_CALLBACK
       extends [netstandard]System.MulticastDelegate
{
  .custom instance void [netstandard]System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute::.ctor(valuetype [netstandard]System.Runtime.InteropServices.CallingConvention) = ( 01 00 01 00 00 00 00 00 ) 
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(object 'object',
                               native int 'method') runtime managed
  {
  } // end of method __PFC_CONFIGCHANGE_CALLBACK::.ctor

  .method public hidebysig newslot virtual 
          instance void  Invoke([in] uint32 id,
                                [in] uint32 sequence,
                                [in][opt] void* context) runtime managed
  {
  } // end of method __PFC_CONFIGCHANGE_CALLBACK::Invoke

} // end of class OSClient.FeatureTuning.__PFC_CONFIGCHANGE_CALLBACK

.class public abstract auto autochar sealed beforefieldinit OSClient.FeatureTuning.Apis
       extends [netstandard]System.Object
{
  .field public static literal int32 __FC_tson_config_version = int32(0x00000000)
  .method public hidebysig static pinvokeimpl("kernelbase.dll" nomangle winapi) 
          valuetype OSClient.FeatureTuning.__FC_System_Flags 
          __FC_Query_System() cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("kernelbase.dll" nomangle winapi) 
          void  __FC_Report([in] uint32 id,
                            [in] valuetype OSClient.FeatureTuning.__FC_Report_Kind kind,
                            [in] uint32 flight_id,
                            [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.PSTR data,
                            [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT hr,
                            [in] void* module_address) cil managed
  {
    .param [4]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
  }
  .method public hidebysig static pinvokeimpl("kernelbase.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          __FC_Query_Config([in] uint32 id,
                            [in] valuetype OSClient.FeatureTuning.__FC_Query_Flags 'request',
                            [out] valuetype OSClient.FeatureTuning.__FC_Config_Info* info) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("kernelbase.dll" nomangle winapi) 
          valuetype OSClient.FeatureTuning.__FC_CONFIGCHANGE_SUBSCRIPTION 
          __FC_Subscribe([in] uint32 id,
                         [in] uint32 sequence,
                         [in] class OSClient.FeatureTuning.__PFC_CONFIGCHANGE_CALLBACK callback,
                         [in][opt] void* context) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("kernelbase.dll" nomangle winapi) 
          void  __FC_Unsubscribe([in] valuetype OSClient.FeatureTuning.__FC_CONFIGCHANGE_SUBSCRIPTION subscription) cil managed
  {
  }
} // end of class OSClient.FeatureTuning.Apis

.class interface public abstract auto ansi OSClient.UDK.ICallingProcessInfo
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 B9 A1 C6 68 39 DE C3 42 8D 28 BF 40 A5 12   // .....h9..B.(.@..
                                                                                          65 41 00 00 )                                     // eA..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          OpenCallerProcessHandle([in] uint32 desiredAccess,
                                  [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HANDLE* callerPocessHandle) cil managed
  {
  } // end of method ICallingProcessInfo::OpenCallerProcessHandle

} // end of class OSClient.UDK.ICallingProcessInfo

.class public abstract auto autochar sealed beforefieldinit OSClient.UDK.Apis
       extends [netstandard]System.Object
{
  .method public hidebysig static pinvokeimpl("combase.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          CoGetCallContextOfObject([in] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown server,
                                   [in] valuetype [netstandard]System.Guid* riid,
                                   [out] void** ppv) cil managed
  {
    .param [2]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
    .param [3]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ComOutPtrAttribute::.ctor() = ( 01 00 00 00 ) 
  }
  .method public hidebysig static pinvokeimpl("combase.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          CoImpersonateClientOfObject([in] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown server,
                                      [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.BOOL* foundCallForServer) cil managed
  {
  }
  .method public hidebysig static pinvokeimpl("sechost.dll" nomangle winapi) 
          valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          CapabilityCheck([in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HANDLE Token,
                          [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.PWSTR CapabilityId,
                          [out] uint8* HasCapability) cil managed
  {
    .param [2]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
  }
} // end of class OSClient.UDK.Apis

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2Interop
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 A7 34 2B 91 0B D1 C4 49 AF 18 7C B7 E6 04   // ...4+....I..|...
                                                                                          E0 1A 00 00 ) 
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          AddHostObjectToScript([in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.PWSTR name,
                                [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.System.Variant.VARIANT* 'object') cil managed
  {
    .param [1]
    .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.ConstAttribute::.ctor() = ( 01 00 00 00 ) 
  } // end of method ICoreWebView2Interop::AddHostObjectToScript

} // end of class OSClient.WebView2.ICoreWebView2Interop

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2CompositionControllerInterop
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 CE 22 99 8E 80 9C E6 42 BA D7 FC EB F2 91   // ...".....B......
                                                                                          A4 95 00 00 ) 
  .method public hidebysig newslot specialname abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          get_AutomationProvider([out] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown* provider) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop::get_AutomationProvider

  .method public hidebysig newslot specialname abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          get_RootVisualTarget([out] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown* target) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop::get_RootVisualTarget

  .method public hidebysig newslot specialname abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          put_RootVisualTarget([in] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown target) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop::put_RootVisualTarget

} // end of class OSClient.WebView2.ICoreWebView2CompositionControllerInterop

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2CompositionControllerInterop2
       implements OSClient.WebView2.ICoreWebView2CompositionControllerInterop
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 E1 BB 47 6B 80 24 F8 4F A5 BA 69 C2 F0 B8   // ....Gk.$.O..i...
                                                                                          68 B3 00 00 )                                     // h...
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          DragEnter([in] class [Windows.Win32.winmd]Windows.Win32.System.Com.IDataObject dataObject,
                    [in] uint32 keyState,
                    [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.POINT point,
                    [in][out] uint32* effect) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop2::DragEnter

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          DragLeave() cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop2::DragLeave

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          DragOver([in] uint32 keyState,
                   [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.POINT point,
                   [in][out] uint32* effect) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop2::DragOver

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          Drop([in] class [Windows.Win32.winmd]Windows.Win32.System.Com.IDataObject dataObject,
               [in] uint32 keyState,
               [in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.POINT point,
               [in][out] uint32* effect) cil managed
  {
  } // end of method ICoreWebView2CompositionControllerInterop2::Drop

} // end of class OSClient.WebView2.ICoreWebView2CompositionControllerInterop2

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2EnvironmentInterop
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 63 3A 50 EE E2 C1 BF 4F 8A 4D 82 4E 95 F8   // ..c:P....O.M.N..
                                                                                          BB 13 00 00 ) 
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetAutomationProviderForWindow([in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HWND hwnd,
                                         [out] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown* provider) cil managed
  {
  } // end of method ICoreWebView2EnvironmentInterop::GetAutomationProviderForWindow

} // end of class OSClient.WebView2.ICoreWebView2EnvironmentInterop

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncFinishedHandler
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 29 E4 A1 32 62 C3 B3 48 83 2D 4A 89 58 10   // ..)..2b..H.-J.X.
                                                                                          C5 73 00 00 )                                     // .s..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          Invoke([in] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT errorCode,
                 [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.System.Variant.VARIANT* result) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchAsyncFinishedHandler::Invoke

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncFinishedHandler

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncResult
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 B3 D0 ED 79 40 1E 58 4A B7 71 FE 8B 4F 38   // .....y@.XJ.q..O8
                                                                                          0D F9 00 00 ) 
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          SetCompletedHandler([in] class OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncFinishedHandler completedCallback) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchAsyncResult::SetCompletedHandler

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncResult

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncResult2
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 E1 62 FE 82 09 A0 7C 42 8D 1C 59 B0 A3 77   // ...b....|B..Y..w
                                                                                          B7 3D 00 00 )                                     // .=..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          Cancel() cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchAsyncResult2::Cancel

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchAsyncResult2

.class public auto ansi sealed OSClient.WebView2.DispatchContainerScriptBehavior
       extends [netstandard]System.Enum
{
  .field public specialname rtspecialname int32 value__
  .field public static literal valuetype OSClient.WebView2.DispatchContainerScriptBehavior DispatchContainerScriptBehaviorNone = int32(0x00000000)
  .field public static literal valuetype OSClient.WebView2.DispatchContainerScriptBehavior DispatchContainerScriptBehaviorVector = int32(0x00000001)
  .field public static literal valuetype OSClient.WebView2.DispatchContainerScriptBehavior DispatchContainerScriptBehaviorMap = int32(0x00000002)
} // end of class OSClient.WebView2.DispatchContainerScriptBehavior

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchContainer
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 95 45 32 2E A2 31 97 47 9F 23 A6 57 B2 2A   // ...E2..1.G.#.W.*
                                                                                          1E 4B 00 00 )                                     // .K..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetInnerObject([out] class [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown* 'object') cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer::GetInnerObject

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetBasisVariant([in][out] valuetype [Windows.Win32.winmd]Windows.Win32.System.Variant.VARIANT* basis) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer::GetBasisVariant

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetPropertyNames([in][out] uint16*** names,
                           [in][out] native uint* namesLength) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer::GetPropertyNames

  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetScriptBehavior([in][out] uint32* behaviorIdx) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer::GetScriptBehavior

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchContainer

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchContainer2
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 6B 92 9F 34 7F 22 EF 40 A3 59 B9 41 25 3E   // ..k..4.".@.Y.A%>
                                                                                          D0 D3 00 00 ) 
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          IsPropertyCacheable([in] int32 propertyId,
                              [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.BOOL* isCacheable) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer2::IsPropertyCacheable

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchContainer2

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchContainer3
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 5F DC 51 62 65 B5 A6 43 83 E4 39 CD 5E 9D   // .._.Qbe..C..9.^.
                                                                                          97 29 00 00 )                                     // .)..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetPropertiesPrecacheability([in][out] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.BOOL** boolsOut,
                                       [in][out] native uint* propertiesLengthOut) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer3::GetPropertiesPrecacheability

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchContainer3

.class interface public abstract auto ansi OSClient.WebView2.ICoreWebView2PrivateDispatchContainer4
       implements [Windows.Win32.winmd]Windows.Win32.System.Com.IUnknown
{
  .custom instance void [Windows.Win32.winmd]Windows.Win32.Foundation.Metadata.GuidAttribute::.ctor(uint32,
                                                                               uint16,
                                                                               uint16,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8,
                                                                               uint8) = ( 01 00 01 0E 97 E4 08 80 92 40 9C 5C 28 D1 47 39   // .........@.\(.G9
                                                                                          4F 41 00 00 )                                     // OA..
  .method public hidebysig newslot abstract virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.HRESULT 
          GetOutArrayParameterInfo([in][out] int32** indexes,
                                   [in][out] valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.PWSTR** names,
                                   [in][out] native uint* length) cil managed
  {
  } // end of method ICoreWebView2PrivateDispatchContainer4::GetOutArrayParameterInfo

} // end of class OSClient.WebView2.ICoreWebView2PrivateDispatchContainer4

.class public abstract auto autochar sealed beforefieldinit OSClient.WebView2.Apis
       extends [netstandard]System.Object
{
} // end of class OSClient.WebView2.Apis

.class public sequential ansi sealed beforefieldinit OSClient.WNF._WNF_OPAQUE_GUID
       extends [netstandard]System.ValueType
{
  .field public uint8[16] _bytes
} // end of class OSClient.WNF._WNF_OPAQUE_GUID

.class public sequential ansi sealed beforefieldinit OSClient.WNF.WNF_STATE_NAME
       extends [netstandard]System.ValueType
{
  .field public uint32[2] Data
} // end of class OSClient.WNF.WNF_STATE_NAME

.class public sequential ansi sealed beforefieldinit OSClient.WNF._WNF_TYPE_ID
       extends [netstandard]System.ValueType
{
  .field public valuetype OSClient.WNF._WNF_OPAQUE_GUID TypeId
} // end of class OSClient.WNF._WNF_TYPE_ID

.class public auto autochar sealed beforefieldinit OSClient.WNF.PWNF_USER_CALLBACK
       extends [netstandard]System.MulticastDelegate
{
  .custom instance void [netstandard]System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute::.ctor(valuetype [netstandard]System.Runtime.InteropServices.CallingConvention) = ( 01 00 01 00 00 00 00 00 ) 
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(object 'object',
                               native int 'method') runtime managed
  {
  } // end of method PWNF_USER_CALLBACK::.ctor

  .method public hidebysig newslot virtual 
          instance valuetype [Windows.Win32.winmd]Windows.Win32.Foundation.NTSTATUS 
          Invoke() runtime managed
  {
  } // end of method PWNF_USER_CALLBACK::Invoke

} // end of class OSClient.WNF.PWNF_USER_CALLBACK


// =============================================================

// *********** DISASSEMBLY COMPLETE ***********************
