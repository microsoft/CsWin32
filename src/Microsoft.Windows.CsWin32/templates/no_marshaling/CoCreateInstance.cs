/// <inheritdoc cref="CoCreateInstance(Guid*, IUnknown*, uint, Guid*, void**)"/>
internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, IUnknown* pUnkOuter, uint dwClsContext, out T* ppv)
    where T : unmanaged
{
    HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out void* o);
    ppv = (T*)o;
    return hr;
}
