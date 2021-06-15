/// <inheritdoc cref="CoCreateInstance(Guid*, object, win32.System.Com.CLSCTX, Guid*, out object)"/>
internal static unsafe win32.Foundation.HRESULT CoCreateInstance<T>(in Guid rclsid, object pUnkOuter, win32.System.Com.CLSCTX dwClsContext, out T ppv)
    where T : class
{
    win32.Foundation.HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out object o);
    ppv = (T)o;
    return hr;
}
