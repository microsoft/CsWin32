partial struct BOOLEAN
{
	internal BOOLEAN(bool value) => this.Value = value ? 1 : 0;
	public static implicit operator bool(BOOLEAN value) => this.value != 0;
	public static implicit operator BOOLEAN(bool value) => new BOOLEAN(value);
}
