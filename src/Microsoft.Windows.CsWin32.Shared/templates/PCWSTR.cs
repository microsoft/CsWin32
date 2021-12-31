﻿/// <summary>
/// A pointer to a constant character string.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PCWSTR
	: IEquatable<PCWSTR>
{
	/// <summary>
	/// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
	/// </summary>
	internal readonly char* Value;
	internal PCWSTR(char* value) => this.Value = value;
	public static explicit operator char*(PCWSTR value) => value.Value;
	public static implicit operator PCWSTR(char* value) => new PCWSTR(value);
	public static implicit operator PCWSTR(PWSTR value) => new PCWSTR(value.Value);
	public bool Equals(PCWSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PCWSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <summary>
	/// Gets the number of characters up to the first null character (exclusive).
	/// </summary>
	internal int Length
	{
		get
		{
			char* p = this.Value;
			if (p is null)
				return 0;
			while (*p != '\0')
				p++;
			return checked((int)(p - this.Value));
		}
	}

	/// <summary>
	/// Returns a <see langword="string"/> with a copy of this character array.
	/// </summary>
	/// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
	public override string ToString() => this.Value is null ? null : new string(this.Value);

	private string DebuggerDisplay => this.ToString();
}
