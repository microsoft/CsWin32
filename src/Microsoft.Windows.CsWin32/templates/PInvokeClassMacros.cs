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
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/winerror/nf-winerror-hresult_from_win32">the documentation for this API</see>.
	/// </remarks>
	internal static global::Windows.Win32.Foundation.HRESULT HRESULT_FROM_WIN32(global::Windows.Win32.Foundation.WIN32_ERROR error) => new(unchecked(error <= 0 ? (int)error : (int)(((uint)error & 0x0000FFFF) | 0x80070000)));

	/// <summary>
	/// Joins two 16-bit integers into a 32-bit integer.
	/// </summary>
	/// <param name="a">The low word.</param>
	/// <param name="b">The high word.</param>
	/// <returns>A 32-bit unsigned integer.</returns>
	internal static uint MAKELONG(ushort a, ushort b) => unchecked((uint)(a | b << 16));

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
	internal static ushort HIWORD(uint value) => unchecked((ushort)(value >> 16));

	/// <summary>
	/// Retrieves the signed x-coordinate from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The signed x-coordinate.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/windowsx/nf-windowsx-get_x_lparam">the documentation for this API</see>.
	/// </remarks>
	internal static int GET_X_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => unchecked((int)(short)LOWORD(unchecked((uint)(nint)lParam)));

	/// <summary>
	/// Retrieves the signed y-coordinate from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The signed y-coordinate.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/windowsx/nf-windowsx-get_y_lparam">the documentation for this API</see>.
	/// </remarks>
	internal static int GET_Y_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => unchecked((int)(short)HIWORD(unchecked((uint)(nint)lParam)));

	/// <summary>
	/// Retrieves a <see cref="global::Windows.Win32.Foundation.POINTS"/> structure from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>A POINTS structure containing the x and y coordinates.</returns>
	internal static global::Windows.Win32.Foundation.POINTS MAKEPOINTS(global::Windows.Win32.Foundation.LPARAM lParam) => new global::Windows.Win32.Foundation.POINTS { x = unchecked((short)LOWORD(unchecked((uint)(nint)lParam))), y = unchecked((short)HIWORD(unchecked((uint)(nint)lParam))) };

	/// <summary>
	/// Retrieves the signed wheel delta value from the specified <see cref="global::Windows.Win32.Foundation.WPARAM"/> value.
	/// </summary>
	/// <param name="wParam">The value to be converted.</param>
	/// <returns>The signed wheel delta value.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/inputdev/wm-mousewheel">the documentation for this API</see>.
	/// </remarks>
	internal static short GET_WHEEL_DELTA_WPARAM(global::Windows.Win32.Foundation.WPARAM wParam) => unchecked((short)HIWORD(unchecked((uint)(nuint)wParam)));

	/// <summary>
	/// Retrieves the application command from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The application command.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-get_appcommand_lparam">the documentation for this API</see>.
	/// </remarks>
	internal static short GET_APPCOMMAND_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => unchecked((short)(HIWORD(unchecked((uint)(nint)lParam)) & ~0xF000));

	/// <summary>
	/// Retrieves the input device type from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The input device type.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-get_device_lparam">the documentation for this API</see>.
	/// </remarks>
	internal static ushort GET_DEVICE_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => unchecked((ushort)(HIWORD(unchecked((uint)(nint)lParam)) & 0xF000));

	/// <summary>
	/// Retrieves the key state flags from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The key state flags.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-get_flags_lparam">the documentation for this API</see>.
	/// </remarks>
	internal static ushort GET_FLAGS_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => LOWORD(unchecked((uint)(nint)lParam));

	/// <summary>
	/// Retrieves the key state from the specified <see cref="global::Windows.Win32.Foundation.LPARAM"/> value.
	/// </summary>
	/// <param name="lParam">The value to be converted.</param>
	/// <returns>The key state.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/inputdev/wm-appcommand">the documentation for this API</see>.
	/// </remarks>
	internal static ushort GET_KEYSTATE_LPARAM(global::Windows.Win32.Foundation.LPARAM lParam) => LOWORD(unchecked((uint)(nint)lParam));

	/// <summary>
	/// Retrieves the key state from the specified <see cref="global::Windows.Win32.Foundation.WPARAM"/> value.
	/// </summary>
	/// <param name="wParam">The value to be converted.</param>
	/// <returns>The key state.</returns>
	internal static ushort GET_KEYSTATE_WPARAM(global::Windows.Win32.Foundation.WPARAM wParam) => LOWORD(unchecked((uint)(nuint)wParam));

	/// <summary>
	/// Retrieves the hit-test value from the specified <see cref="global::Windows.Win32.Foundation.WPARAM"/> value.
	/// </summary>
	/// <param name="wParam">The value to be converted.</param>
	/// <returns>The hit-test value.</returns>
	/// <remarks>
	/// Learn more in <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-get_nchittest_wparam">the documentation for this API</see>.
	/// </remarks>
	internal static short GET_NCHITTEST_WPARAM(global::Windows.Win32.Foundation.WPARAM wParam) => unchecked((short)LOWORD(unchecked((uint)(nuint)wParam)));

	/// <summary>
	/// Retrieves the input code from the specified <see cref="global::Windows.Win32.Foundation.WPARAM"/> value.
	/// </summary>
	/// <param name="wParam">The value to be converted.</param>
	/// <returns>The input code.</returns>
	internal static uint GET_RAWINPUT_CODE_WPARAM(global::Windows.Win32.Foundation.WPARAM wParam) => unchecked((uint)(nuint)wParam);
}
