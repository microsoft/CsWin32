/// <inheritdoc cref="CoCreateInstance(Guid*, IUnknown*, CLSCTX, Guid*, void**)"/>
internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, IUnknown* pUnkOuter, CLSCTX dwClsContext, out T* ppv)
    where T : unmanaged
{
    HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out void* o);
    ppv = (T*)o;
    return hr;
}
