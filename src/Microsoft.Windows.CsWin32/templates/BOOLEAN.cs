partial struct BOOLEAN
{
	internal unsafe BOOLEAN(bool value) => this.Value = value ? 1 : 0;
	public static unsafe implicit operator bool(BOOLEAN value)
	{
		byte v = checked((byte)value.Value);
		return *(bool*)&v;
	}

	public static implicit operator BOOLEAN(bool value) => new BOOLEAN(value);
}
