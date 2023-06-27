internal unsafe interface IVTable<TComInterface, TVTable> : IVTable
	where TVTable : unmanaged
	where TComInterface : unmanaged, IVTable<TComInterface, TVTable>
{
	private protected static abstract void PopulateVTable(TVTable* vtable);

	static System.Com.IUnknown.Vtbl* IVTable.VTable { get; } = (System.Com.IUnknown.Vtbl*)CreateVTable();

	private static TVTable* CreateVTable()
	{
		TVTable* vtbl = (TVTable*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(TVTable), sizeof(TVTable));
		ComHelpers.PopulateIUnknown<TComInterface>((System.Com.IUnknown.Vtbl*)vtbl);
		TComInterface.PopulateVTable(vtbl);
		return vtbl;
	}
}
