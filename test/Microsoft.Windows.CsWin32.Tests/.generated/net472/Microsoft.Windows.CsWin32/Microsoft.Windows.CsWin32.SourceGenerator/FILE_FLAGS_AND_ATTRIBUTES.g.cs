﻿// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

#pragma warning disable CS1591,CS1573,CS0465,CS0649,CS8019,CS1570,CS1584,CS1658
namespace Windows.Win32
{
	using global::System;
	using global::System.Diagnostics;
	using global::System.Runtime.CompilerServices;
	using global::System.Runtime.InteropServices;
	using global::System.Runtime.Versioning;
	using win32 = global::Windows.Win32;

	namespace Storage.FileSystem
	{
		[Flags]
		internal enum FILE_FLAGS_AND_ATTRIBUTES : uint
		{
			FILE_ATTRIBUTE_READONLY = 0x00000001,
			FILE_ATTRIBUTE_HIDDEN = 0x00000002,
			FILE_ATTRIBUTE_SYSTEM = 0x00000004,
			FILE_ATTRIBUTE_DIRECTORY = 0x00000010,
			FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
			FILE_ATTRIBUTE_DEVICE = 0x00000040,
			FILE_ATTRIBUTE_NORMAL = 0x00000080,
			FILE_ATTRIBUTE_TEMPORARY = 0x00000100,
			FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,
			FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,
			FILE_ATTRIBUTE_COMPRESSED = 0x00000800,
			FILE_ATTRIBUTE_OFFLINE = 0x00001000,
			FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
			FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
			FILE_ATTRIBUTE_INTEGRITY_STREAM = 0x00008000,
			FILE_ATTRIBUTE_VIRTUAL = 0x00010000,
			FILE_ATTRIBUTE_NO_SCRUB_DATA = 0x00020000,
			FILE_ATTRIBUTE_EA = 0x00040000,
			FILE_ATTRIBUTE_PINNED = 0x00080000,
			FILE_ATTRIBUTE_UNPINNED = 0x00100000,
			FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000,
			FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000,
			FILE_FLAG_WRITE_THROUGH = 0x80000000,
			FILE_FLAG_OVERLAPPED = 0x40000000,
			FILE_FLAG_NO_BUFFERING = 0x20000000,
			FILE_FLAG_RANDOM_ACCESS = 0x10000000,
			FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
			FILE_FLAG_DELETE_ON_CLOSE = 0x04000000,
			FILE_FLAG_BACKUP_SEMANTICS = 0x02000000,
			FILE_FLAG_POSIX_SEMANTICS = 0x01000000,
			FILE_FLAG_SESSION_AWARE = 0x00800000,
			FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000,
			FILE_FLAG_OPEN_NO_RECALL = 0x00100000,
			FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000,
			SECURITY_ANONYMOUS = 0x00000000,
			SECURITY_IDENTIFICATION = 0x00010000,
			SECURITY_IMPERSONATION = 0x00020000,
			SECURITY_DELEGATION = 0x00030000,
			SECURITY_CONTEXT_TRACKING = 0x00040000,
			SECURITY_EFFECTIVE_ONLY = 0x00080000,
			SECURITY_SQOS_PRESENT = 0x00100000,
			SECURITY_VALID_SQOS_FLAGS = 0x001F0000,
		}
	}
}
