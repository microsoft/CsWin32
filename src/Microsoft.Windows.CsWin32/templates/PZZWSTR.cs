/// <summary>
/// A pointer to an empty-string terminated list of null-terminated strings that uses UTF-16 encoding.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
internal unsafe readonly partial struct PZZWSTR
	: IEquatable<PZZWSTR>
{
	/// <summary>
	/// A pointer to the first character in the string.
	/// </summary>
	internal readonly char* Value;
	internal PZZWSTR(char* value) => this.Value = value;
	public static explicit operator char*(PZZWSTR value) => value.Value;
	public static implicit operator PZZWSTR(char* value) => new PZZWSTR(value);
	public static implicit operator PCZZWSTR(PZZWSTR value) => new PCZZWSTR(value.Value);
	public bool Equals(PZZWSTR other) => this.Value == other.Value;
	public override bool Equals(object obj) => obj is PZZWSTR other && this.Equals(other);
	public override int GetHashCode() => unchecked((int)this.Value);

	/// <inheritdoc cref="PCZZWSTR.Length"/>
	internal int Length => new PCZZWSTR(this.Value).Length;

	/// <inheritdoc cref="PCZZWSTR.ToString()"/>
	public override string ToString() => new PCZZWSTR(this.Value).ToString();

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal Span<char> AsSpan() => this.Value is null ? default(Span<char>) : new Span<char>(this.Value, this.Length);
#endif

	private string DebuggerDisplay => this.ToString();
}
