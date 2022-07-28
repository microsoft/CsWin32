partial struct POINT
{
	internal POINT(global::System.Drawing.Point value) : this(value.X, value.Y) { }
	internal POINT(int x, int y)
	{
		this.x = x;
		this.y = y;
	}

	internal bool IsEmpty => x == 0 && y == 0;
	public static implicit operator global::System.Drawing.Point(POINT value) => new global::System.Drawing.Point(value.x, value.y);
	public static implicit operator POINT(global::System.Drawing.Point value) => new POINT(value);
}
