partial struct POINT
{
	internal POINT(global::System.Drawing.Point value)
	{
		this.x = value.X;
		this.y = value.Y;
	}

	public static implicit operator global::System.Drawing.Point(POINT value) => new global::System.Drawing.Point(value.x, value.y);
	public static implicit operator POINT(global::System.Drawing.Point value) => new POINT(value);
}
