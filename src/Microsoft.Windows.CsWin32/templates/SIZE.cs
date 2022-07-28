partial struct SIZE
{
	internal SIZE(global::System.Drawing.Size value)
	{
		this.cx = value.Width;
		this.cy = value.Height;
	}

	public int Width => this.cx;
	public int Height => this.cy;
	public bool IsEmpty => this.cx == 0 && this.cy == 0;
	public static implicit operator global::System.Drawing.Size(SIZE value) => new global::System.Drawing.Size(value.cx, value.cy);
	public static implicit operator SIZE(global::System.Drawing.Size value) => new SIZE(value);
}
