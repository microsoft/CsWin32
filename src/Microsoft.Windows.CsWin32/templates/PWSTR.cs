partial struct PWSTR
{
	public static implicit operator PCWSTR(PWSTR value) => new PCWSTR(value.Value);

	/// <inheritdoc cref="PCWSTR.Length"/>
	internal int Length => new PCWSTR(this.Value).Length;

	/// <inheritdoc cref="PCWSTR.ToString()"/>
	public override string ToString() => new PCWSTR(this.Value).ToString();

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal Span<char> AsSpan() => this.Value is null ? default(Span<char>) : new Span<char>(this.Value, this.Length);
#endif
}
