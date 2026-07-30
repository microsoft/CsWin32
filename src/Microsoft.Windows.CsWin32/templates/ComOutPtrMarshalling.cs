/// <summary>
/// Selects the wrapper family used to project the COM output pointer produced by an
/// <c>IID</c>/<c>void**</c> method into a managed object.
/// </summary>
/// <remarks>
/// The projection is chosen from the closed generic type argument of the friendly overload and this
/// policy. The returned object is never inspected, so no additional <c>QueryInterface</c> is issued
/// to discover the wrapper family.
/// </remarks>
internal enum ComOutPtrMarshalling
{
	/// <summary>
	/// Classifies the closed generic type argument once and selects <c>WindowsRuntime</c> for a
	/// C#/WinRT projected type and <c>ComObject</c> for everything else, including <c>object</c>.
	/// </summary>
	Default = 0,

	/// <summary>
	/// Requests <c>IID_IUnknown</c> for <c>object</c> and the type's own IID otherwise, then projects
	/// the result with the identity-cached source-generated COM wrapper.
	/// </summary>
	ComObject = 1,

	/// <summary>
	/// Requests <c>IID_IInspectable</c> for <c>object</c> and the C#/WinRT generated IID otherwise, then
	/// projects the result with the C#/WinRT wrapper. Requires a reference to C#/WinRT.
	/// </summary>
	WindowsRuntime = 2,

	/// <summary>
	/// Behaves like <c>ComObject</c> except that the wrapper is created outside the COM identity cache so
	/// it may be released deterministically without affecting other managed references to the same COM identity.
	/// </summary>
	ComObjectUniqueInstance = 3,
}
