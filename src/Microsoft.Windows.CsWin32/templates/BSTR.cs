internal partial struct BSTR
{
	/// <summary>
	/// Gets the length of the BSTR in characters.
	/// </summary>
	internal unsafe int Length => this.Value is null ? 0 : checked((int)(*(((uint*)this.Value) - 1) / sizeof(char)));
}
