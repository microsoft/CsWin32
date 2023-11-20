partial struct BOOL
{
	public const int Size = sizeof(int);
	public static BOOL TRUE { get; } = new(true);
	public static BOOL FALSE { get; } = new(false);
	internal BOOL(bool value) => this.Value = value ? 1 : 0;
	public static implicit operator bool(BOOL value) => value.Value != 0;
	public static implicit operator BOOL(bool value) => new BOOL(value);
	public static bool operator true(BOOL value) => value.Value != 0;
	public static bool operator false(BOOL value) => value.Value == 0;
}
