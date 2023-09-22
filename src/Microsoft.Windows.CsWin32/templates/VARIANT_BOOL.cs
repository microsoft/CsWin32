partial struct VARIANT_BOOL
{
	internal VARIANT_BOOL(bool value) => this.Value = value ? VARIANT_TRUE : VARIANT_FALSE;
	public static implicit operator bool(VARIANT_BOOL value) => value != VARIANT_FALSE;
	public static implicit operator VARIANT_BOOL(bool value) => value ? VARIANT_TRUE : VARIANT_FALSE;
}
