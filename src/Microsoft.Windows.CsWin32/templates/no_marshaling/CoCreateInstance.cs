/// <inheritdoc cref="CoCreateInstance(Guid*, global::Windows.Win32.System.Com.IUnknown*, global::Windows.Win32.System.Com.CLSCTX, Guid*, void**)"/>
internal static unsafe global::Windows.Win32.Foundation.HRESULT CoCreateInstance<T>(in Guid rclsid, global::Windows.Win32.System.Com.IUnknown* pUnkOuter, global::Windows.Win32.System.Com.CLSCTX dwClsContext, out T* ppv)
	where T : unmanaged
{
	global::Windows.Win32.Foundation.HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out void* o);
	ppv = (T*)o;
	return hr;
}
