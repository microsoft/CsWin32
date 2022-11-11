/// <summary>
/// A pointer to a constant, empty-string terminated list of null-terminated strings with 1-byte characters (often UTF-8).
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PCZZSTR
	: IEquatable<PCZZSTR>
{
	/// <summary>
	/// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
	/// </summary>
	internal readonly byte* Value;
	internal PCZZSTR(byte* value) => this.Value = value;
	public static implicit operator byte*(PCZZSTR value) => value.Value;
	public static explicit operator PCZZSTR(byte* value) => new PCZZSTR(value);
	public bool Equals(PCZZSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PCZZSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <summary>
	/// Gets the number of characters in this null-terminated string list, excluding the final null terminator.
	/// </summary>
	internal int Length
	{
		get
		{
			PCSTR str = new PCSTR(this.Value);
			while (true)
			{
				int len = str.Length;
				if (len > 0)
				{
					str = new PCSTR(str.Value + len + 1);
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
	/// Returns a <see langword="string"/> with a copy of this character array, decoding as UTF-8.
	/// </summary>
	/// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
	public override string ToString() => this.Value is null ? null : new string((sbyte*)this.Value, 0, this.Length, global::System.Text.Encoding.Default);

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal ReadOnlySpan<byte> AsSpan() => this.Value is null ? default(ReadOnlySpan<byte>) : new ReadOnlySpan<byte>(this.Value, this.Length);
#endif

	private string DebuggerDisplay => this.ToString();
}
