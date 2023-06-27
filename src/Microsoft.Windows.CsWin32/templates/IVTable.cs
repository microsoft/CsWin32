/// <summary>
/// Non generic interface that allows constraining against a COM wrapper type directly. COM structs should
/// implement <see cref="IVTable{TComInterface, TVTable}"/>.
/// </summary>
internal unsafe interface IVTable
{
	static abstract System.Com.IUnknown.Vtbl* VTable { get; }
}
