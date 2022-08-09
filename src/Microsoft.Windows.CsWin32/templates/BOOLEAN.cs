partial struct BOOLEAN
{
	internal BOOLEAN(bool value) => this.Value = value ? (byte)1 : (byte)0;
	public static implicit operator bool(BOOLEAN value) => value.Value != 0;
	public static implicit operator BOOLEAN(bool value) => new BOOLEAN(value);
}
