/// <inheritdoc cref="CoCreateInstance(Guid*, object, uint, Guid*, out object)"/>
internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, object pUnkOuter, uint dwClsContext, out T ppv)
    where T : class
{
    HRESULT hr = CoCreateInstance(rclsid, pUnkOuter, dwClsContext, typeof(T).GUID, out object o);
    ppv = (T)o;
    return hr;
}
