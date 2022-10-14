internal partial struct BSTR
{
	/// <summary>
	/// Gets the length of the BSTR in characters.
	/// </summary>
	internal unsafe int Length => this.Value is null ? 0 : checked((int)(*(((uint*)this.Value) - 1) / sizeof(char)));

	public override string ToString() => this.Value != null ? Marshal.PtrToStringBSTR(new IntPtr(this.Value)) : null;

#if canUseSpan
	public static unsafe implicit operator ReadOnlySpan<char>(BSTR bstr) => bstr.Value != null ? new ReadOnlySpan<char>(bstr.Value, *((int*)bstr.Value - 1) / 2) : default(ReadOnlySpan<char>);

	internal ReadOnlySpan<char> AsSpan() => this;
#endif
}
