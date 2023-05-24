partial struct SIZE
{
#if canUseSystemDrawing
	internal SIZE(global::System.Drawing.Size value) : this(value.Width, value.Height) { }
#endif
	internal SIZE(int width, int height)
	{
		this.cx = width;
		this.cy = height;
	}

	internal readonly int Width => this.cx;
	internal readonly int Height => this.cy;
	internal readonly bool IsEmpty => this.cx == 0 && this.cy == 0;
#if canUseSystemDrawing
	public static implicit operator global::System.Drawing.Size(SIZE value) => new global::System.Drawing.Size(value.cx, value.cy);
	public static implicit operator SIZE(global::System.Drawing.Size value) => new SIZE(value);
#endif
}
