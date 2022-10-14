/// <summary>
/// A pointer to a constant, empty-string terminated list of null-terminated strings that uses UTF-16 encoding.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PCZZWSTR
	: IEquatable<PCZZWSTR>
{
	/// <summary>
	/// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
	/// </summary>
	internal readonly char* Value;
	internal PCZZWSTR(char* value) => this.Value = value;
	public static explicit operator char*(PCZZWSTR value) => value.Value;
	public static implicit operator PCZZWSTR(char* value) => new PCZZWSTR(value);
	public bool Equals(PCZZWSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PCZZWSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <summary>
	/// Gets the number of characters in this null-terminated string list, excluding the final null terminator.
	/// </summary>
	internal int Length
	{
		get
		{
			PCWSTR str = new PCWSTR(this.Value);
			while (true)
			{
				int len = str.Length;
				if (len > 0)
				{
					str = new PCWSTR(str.Value + len + 1);
				}
				else
				{
					break;
				}
			}

			return checked((int)(str.Value - this.Value));
		}
	}

	/// <summary>
	/// Returns a <see langword="string"/> with a copy of this character array.
	/// </summary>
	/// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
	public override string ToString() => this.Value is null ? null : new string(this.Value, 0, this.Length);

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal ReadOnlySpan<char> AsSpan() => this.Value is null ? default(ReadOnlySpan<char>) : new ReadOnlySpan<char>(this.Value, this.Length);
#endif

	private string DebuggerDisplay => this.ToString();
}
