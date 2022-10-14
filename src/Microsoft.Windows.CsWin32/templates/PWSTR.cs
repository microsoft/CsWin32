partial struct PWSTR
{
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

	public override string ToString() => this.Value is null ? null : new string(this.Value);

#if canUseSpan
	/// <summary>
	/// Returns a span of the characters in this string.
	/// </summary>
	internal Span<char> AsSpan() => this.Value is null ? default(Span<char>) : new Span<char>(this.Value, this.Length);
#endif
}
