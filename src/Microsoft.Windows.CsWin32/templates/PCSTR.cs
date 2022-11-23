/// <summary>
/// A pointer to a null-terminated, constant, ANSI character string.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PCSTR
	: IEquatable<PCSTR>
{
	/// <summary>
	/// A pointer to the first character in the string. The content should be considered readonly, as it was typed as constant in the SDK.
	/// </summary>
	internal readonly byte* Value;
	internal PCSTR(byte* value) => this.Value = value;
	public static implicit operator byte*(PCSTR value) => value.Value;
	public static explicit operator PCSTR(byte* value) => new PCSTR(value);
	public bool Equals(PCSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PCSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <summary>
	/// Gets the number of characters up to the first null character (exclusive).
	/// </summary>
	internal int Length
	{
		get
		{
			byte* p = this.Value;
			if (p is null)
				return 0;
			while (*p != 0)
				p++;
			return checked((int)(p - this.Value));
		}
	}

	/// <summary>
	/// Returns a <see langword="string"/> with a copy of this character array, decoding as UTF-8.
	/// </summary>
	/// <returns>A <see langword="string"/>, or <see langword="null"/> if <see cref="Value"/> is <see langword="null"/>.</returns>
	public override string ToString() => this.Value is null ? null : new string((sbyte*)this.Value, 0, this.Length, global::System.Text.Encoding.Default);

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string, up to the first null character (exclusive).
	/// </summary>
	internal ReadOnlySpan<byte> AsSpan() => this.Value is null ? default(ReadOnlySpan<byte>) : new ReadOnlySpan<byte>(this.Value, this.Length);
#endif

	private string DebuggerDisplay => this.ToString();
}
