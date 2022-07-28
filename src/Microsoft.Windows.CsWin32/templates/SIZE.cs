partial struct SIZE
{
	internal SIZE(global::System.Drawing.Size value)
	{
		this.cx = value.Width;
		this.cy = value.Height;
	}

	public static implicit operator global::System.Drawing.Size(SIZE value) => new global::System.Drawing.Size(value.cx, value.cy);
	public static implicit operator SIZE(global::System.Drawing.Size value) => new SIZE(value);
}
