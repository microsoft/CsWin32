/// <inheritdoc cref="CoCreateInstance(Guid*, win32.System.Com.IUnknown*, win32.System.Com.CLSCTX, Guid*, void**)"/>
internal static unsafe win32.System.Com.HRESULT CoCreateInstance<T>(in Guid rclsid, win32.System.Com.IUnknown* pUnkOuter, win32.System.Com.CLSCTX dwClsContext, out T* ppv)
    where T : unmanaged
{
    win32.System.Com.HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out void* o);
    ppv = (T*)o;
    return hr;
}
