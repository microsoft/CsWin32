internal static unsafe partial class ComHelpers
{
	private static readonly winmdroot.Foundation.HRESULT COR_E_OBJECTDISPOSED = (winmdroot.Foundation.HRESULT)unchecked((int)0x80131622);
	private static readonly winmdroot.Foundation.HRESULT S_OK = (winmdroot.Foundation.HRESULT)0;

	internal static winmdroot.Foundation.HRESULT UnwrapCCW<TThis, TInterface>(TThis* @this, out TInterface @object)
		where TThis : unmanaged
		where TInterface : class
	{
		@object = ComWrappers.ComInterfaceDispatch.GetInstance<TInterface>((ComWrappers.ComInterfaceDispatch*)@this);
		return @object is null ? COR_E_OBJECTDISPOSED : S_OK;
	}

#if canUseInterfaceStaticMembers
	internal static void PopulateIUnknown<TComInterface>(System.Com.IUnknown.Vtbl* vtable)
		where TComInterface : unmanaged
	{
		PopulateIUnknownImpl<TComInterface>(vtable);
		if (vtable->QueryInterface_1 is null)
		{
			throw new NotImplementedException("v-tables cannot be accessed unless the Windows.Win32.ComHelpers.PopulateIUnknownImpl partial method is implemented.");
		}
	}

	static partial void PopulateIUnknownImpl<TComInterface>(System.Com.IUnknown.Vtbl* vtable)
		where TComInterface : unmanaged;
#endif
}
