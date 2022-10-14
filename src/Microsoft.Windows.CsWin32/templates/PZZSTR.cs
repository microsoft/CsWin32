/// <summary>
/// A pointer to an empty-string terminated list of null-terminated strings with 1-byte characters (often UTF-8).
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PZZSTR
	: IEquatable<PZZSTR>
{
	/// <summary>
	/// A pointer to the first character in the string.
	/// </summary>
	internal readonly byte* Value;
	internal PZZSTR(byte* value) => this.Value = value;
	public static implicit operator byte*(PZZSTR value) => value.Value;
	public static explicit operator PZZSTR(byte* value) => new PZZSTR(value);
	public static implicit operator PCZZSTR(PZZSTR value) => new PCZZSTR(value.Value);
	public bool Equals(PZZSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PZZSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <inheritdoc cref="PCZZSTR.Length"/>
	internal int Length => new PCZZSTR(this.Value).Length;

	/// <inheritdoc cref="PCZZSTR.ToString()"/>
	public override string ToString() => new PCZZSTR(this.Value).ToString();

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal Span<byte> AsSpan() => this.Value is null ? default(Span<byte>) : new Span<byte>(this.Value, this.Length);
#endif

	private string DebuggerDisplay => this.ToString();
}
