﻿partial struct BOOL
{
	internal BOOL(bool value) => this.Value = value ? 1 : 0;
	public static implicit operator bool(BOOL value) => this.value != 0;
	public static implicit operator BOOL(bool value) => new BOOL(value);
}
