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
}
