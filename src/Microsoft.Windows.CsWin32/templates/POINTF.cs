partial struct POINTF
{
	internal POINTF(global::System.Drawing.PointF value)
	{
		this.x = value.X;
		this.y = value.Y;
	}

	public static implicit operator global::System.Drawing.PointF(POINTF value) => new global::System.Drawing.PointF(value.x, value.y);
	public static implicit operator POINTF(global::System.Drawing.PointF value) => new POINTF(value);
}
