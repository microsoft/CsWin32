/// <inheritdoc cref="CoCreateInstance(Guid*, object, global::Windows.Win32.System.Com.CLSCTX, Guid*, out object)"/>
internal static unsafe global::Windows.Win32.Foundation.HRESULT CoCreateInstance<T>(in Guid rclsid, object pUnkOuter, global::Windows.Win32.System.Com.CLSCTX dwClsContext, out T ppv)
	where T : class
{
	global::Windows.Win32.Foundation.HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out object o);
	ppv = (T)o;
	return hr;
}
