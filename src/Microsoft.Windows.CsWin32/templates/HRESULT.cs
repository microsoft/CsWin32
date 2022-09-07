/// <summary>
/// Describes an HRESULT error or success condition.
/// </summary>
/// <remarks>
///  HRESULTs are 32 bit values layed out as follows:
/// <code>
///   3 3 2 2 2 2 2 2 2 2 2 2 1 1 1 1 1 1 1 1 1 1
///   1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0
///  +-+-+-+-+-+---------------------+-------------------------------+
///  |S|R|C|N|r|    Facility         |               Code            |
///  +-+-+-+-+-+---------------------+-------------------------------+
///
///  where
///
///      S - Severity - indicates success/fail
///
///          0 - Success
///          1 - Fail (COERROR)
///
///      R - reserved portion of the facility code, corresponds to NT's
///              second severity bit.
///
///      C - reserved portion of the facility code, corresponds to NT's
///              C field.
///
///      N - reserved portion of the facility code. Used to indicate a
///              mapped NT status value.
///
///      r - reserved portion of the facility code. Reserved for internal
///              use. Used to indicate HRESULT values that are not status
///              values, but are instead message ids for display strings.
///
///      Facility - is the facility code
///
///      Code - is the facility's status code
/// </code>
/// </remarks>
partial struct HRESULT
{
	public static implicit operator uint(HRESULT value) => (uint)value.Value;
	public static explicit operator HRESULT(uint value) => new HRESULT((int)value);

	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	internal bool Succeeded => this.Value >= 0;

	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	internal bool Failed => this.Value < 0;

	/// <inheritdoc cref="Marshal.ThrowExceptionForHR(int, IntPtr)" />
	/// <param name="errorInfo">
	/// A pointer to the IErrorInfo interface that provides more information about the
	/// error. You can specify <see cref="IntPtr.Zero"/> to use the current IErrorInfo interface, or
	/// <c>new IntPtr(-1)</c> to ignore the current IErrorInfo interface and construct the exception
	/// just from the error code.
	/// </param>
	/// <returns><see langword="this"/> <see cref="HRESULT"/>, if it does not reflect an error.</returns>
	/// <seealso cref="Marshal.ThrowExceptionForHR(int, IntPtr)"/>
	internal HRESULT ThrowOnFailure(IntPtr errorInfo = default)
	{
		Marshal.ThrowExceptionForHR(this.Value, errorInfo);
		return this;
	}

	public override string ToString() => string.Format(global::System.Globalization.CultureInfo.InvariantCulture, "0x{0:X8}", this.Value);

	internal string ToString(string format, IFormatProvider formatProvider) => ((uint)this.Value).ToString(format, formatProvider);
}
