partial struct BOOLEAN
{
	internal unsafe BOOLEAN(bool value) => this.Value = *(byte*)&value;
	public static unsafe implicit operator bool(BOOLEAN value)
	{
		byte v = checked((byte)value.Value);
		return *(bool*)&v;
	}

	public static implicit operator BOOLEAN(bool value) => new BOOLEAN(value);
}
