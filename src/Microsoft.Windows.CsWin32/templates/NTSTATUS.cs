/// <summary>
/// Win32 return error codes.
/// </summary>
/// <remarks>
/// This values come from https://msdn.microsoft.com/en-us/library/cc704588.aspx
///  Values are 32 bit values laid out as follows:
///   3 3 2 2 2 2 2 2 2 2 2 2 1 1 1 1 1 1 1 1 1 1
///   1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0
///  +---+-+-+-----------------------+-------------------------------+
///  |Sev|C|R|     Facility          |               Code            |
///  +---+-+-+-----------------------+-------------------------------+
///  where
///      Sev - is the severity code
///          00 - Success
///          01 - Informational
///          10 - Warning
///          11 - Error
///      C - is the Customer code flag
///      R - is a reserved bit
///      Facility - is the facility code
///      Code - is the facility's status code
///
/// FacilityCodes 0x5 - 0xF have been allocated by various drivers.
/// The success status codes 0 - 63 are reserved for wait completion status.
/// </remarks>
partial struct NTSTATUS
{
	public static implicit operator uint(NTSTATUS value) => (uint)value.Value;
	public static explicit operator NTSTATUS(uint value) => new NTSTATUS((int)value);

	internal Severity SeverityCode => (Severity)(((uint)this.Value & 0xc0000000) >> 30);

	internal enum Severity
	{
		Success,
		Informational,
		Warning,
		Error,
	}
}
