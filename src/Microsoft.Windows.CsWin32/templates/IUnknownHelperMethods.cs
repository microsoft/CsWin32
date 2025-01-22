internal class IUnknownHelperMethods
{
	internal unsafe global::Windows.Win32.Foundation.HRESULT QueryInterface<T>(out T* ppv)
		where T : unmanaged
	{
		Guid guid = typeof(T).GUID;
		void* pv;
		var hr = this.QueryInterface(&guid, &pv);
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

