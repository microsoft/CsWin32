internal partial struct DECIMAL
{
	public unsafe DECIMAL(decimal value)
	{
		// DECIMAL is layout-compatible with decimal.
		this = *(DECIMAL*)&value;
	}

	public static unsafe implicit operator decimal(DECIMAL value)
	{
		// DECIMAL is layout-compatible with decimal.
		return *(decimal*)&value;
	}

#if NET5_0_OR_GREATER
	public static implicit operator DECIMAL(decimal value) => new DECIMAL(value);
#endif
}
