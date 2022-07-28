partial struct POINTF
{
	internal POINTF(global::System.Drawing.PointF value) : this(value.X, value.Y) { }
	internal POINTF(float x, float y)
	{
		this.x = x;
		this.y = y;
	}

	internal bool IsEmpty => x == 0 && y == 0;
	public static implicit operator global::System.Drawing.PointF(POINTF value) => new global::System.Drawing.PointF(value.x, value.y);
	public static implicit operator POINTF(global::System.Drawing.PointF value) => new POINTF(value);
}
