partial struct BOOL
{
	internal unsafe BOOL(bool value) => this.Value = *(sbyte*)&value;
	public static unsafe implicit operator bool(BOOL value)
	{
		sbyte v = checked((sbyte)value.Value);
		return *(bool*)&v;
	}

	public static implicit operator BOOL(bool value) => new BOOL(value);
}
