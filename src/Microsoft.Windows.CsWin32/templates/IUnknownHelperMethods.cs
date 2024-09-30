internal class IUnknownHelperMethods
{
	internal unsafe global::Windows.Win32.Foundation.HRESULT QueryInterface<T>(out T* ppv)
		where T : unmanaged
	{
		var hr = this.QueryInterface(typeof(T).GUID, out void* pv);
		if (hr.Succeeded)
		{
			ppv = (T*)pv;
		}
		else
		{
			ppv = null;
		}

		return hr;
	}
}

