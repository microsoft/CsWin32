/// <inheritdoc cref="CoCreateInstance(Guid*, object, global::Windows.Win32.System.Com.CLSCTX, Guid*, out object)"/>
#if NET35
        internal static unsafe global::Windows.Win32.Foundation.HRESULT CoCreateInstance<T>(Guid clsid, object pUnkOuter, global::Windows.Win32.System.Com.CLSCTX dwClsContext, out T ppv)
            where T : class

        {
            Guid iid = typeof(T).GUID;
            global::Windows.Win32.Foundation.HRESULT hr = CoCreateInstance(&clsid, pUnkOuter, dwClsContext, &iid, out object o);
                ppv = (T)o;
                return hr;
        }
#else
internal static unsafe global::Windows.Win32.Foundation.HRESULT CoCreateInstance<T>(in Guid rclsid, object pUnkOuter, global::Windows.Win32.System.Com.CLSCTX dwClsContext, out T ppv)
	where T : class
{
	global::Windows.Win32.Foundation.HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out object o);
	ppv = (T)o;
	return hr;
}
#endif
