/// <summary>
/// This class wrapper is stripped so that individual macros can be requested for generation.
/// </summary>
internal class PInvokeClassMacros
{
	/// <summary>
	/// Creates an <see cref="global::Windows.Win32.Foundation.HRESULT"/> that represents a given <see cref="global::Windows.Win32.Foundation.WIN32_ERROR"/>.
	/// </summary>
	/// <param name="error">The win32 error to be wrapped.</param>
	/// <returns>An <see cref="global::Windows.Win32.Foundation.HRESULT"/>.</returns>
	/// <remarks>
	/// Learn more in <see href="https://docs.microsoft.com/windows/win32/api/winerror/nf-winerror-hresult_from_win32">the documentation for this API</see>.
	/// </remarks>
	internal static global::Windows.Win32.Foundation.HRESULT HRESULT_FROM_WIN32(global::Windows.Win32.Foundation.WIN32_ERROR error) => new(unchecked(error <= 0 ? (int)error : (int)(((uint)error & 0x0000FFFF) | 0x80070000)));

	/// <summary>
	/// Joins two 16-bit integers into a 32-bit integer.
	/// </summary>
	/// <param name="a">The low word.</param>
	/// <param name="b">The high word.</param>
	/// <returns>A 32-bit unsigned integer.</returns>
	internal static uint MAKELONG(ushort a, ushort b) => (uint)(a | b << 16);

	/// <summary>
	/// Constructs a <see cref="global::Windows.Win32.Foundation.WPARAM"/> from two 16-bit values.
	/// </summary>
	/// <param name="l">The low word.</param>
	/// <param name="h">The high word.</param>
	/// <returns>The WPARAM value.</returns>
	internal static global::Windows.Win32.Foundation.WPARAM MAKEWPARAM(ushort l, ushort h) => MAKELONG(l, h);

	/// <summary>
	/// Constructs a <see cref="global::Windows.Win32.Foundation.LPARAM"/> from two 16-bit values.
	/// </summary>
	/// <param name="l">The low word.</param>
	/// <param name="h">The high word.</param>
	/// <returns>The LPARAM value.</returns>
	internal static global::Windows.Win32.Foundation.LPARAM MAKELPARAM(ushort l, ushort h) => unchecked((nint)MAKELONG(l, h));

	/// <summary>
	/// Constructs a <see cref="global::Windows.Win32.Foundation.LRESULT"/> from two 16-bit values.
	/// </summary>
	/// <param name="l">The low word.</param>
	/// <param name="h">The high word.</param>
	/// <returns>The LRESULT value.</returns>
	internal static global::Windows.Win32.Foundation.LRESULT MAKELRESULT(ushort l, ushort h) => unchecked((global::Windows.Win32.Foundation.LRESULT)(nint)MAKELONG(l, h));

	/// <summary>
	/// Retrieves the low-order word from the specified 32-bit value.
	/// </summary>
	/// <param name="value">The 32-bit value.</param>
	/// <returns>The low-order word.</returns>
	internal static ushort LOWORD(uint value) => unchecked((ushort)value);

	/// <summary>
	/// Retrieves the high-order word from the specified 32-bit value.
	/// </summary>
	/// <param name="value">The 32-bit value.</param>
	/// <returns>The high-order word.</returns>
	internal static ushort HIWORD(uint value) => (ushort)(value >> 16);
}
