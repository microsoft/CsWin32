partial struct RECT
{
	internal RECT(global::System.Drawing.Rectangle value)
	{
		this.left = value.Left;
		this.top = value.Top;
		this.right = value.Right;
		this.bottom = value.Bottom;
	}

	public static implicit operator global::System.Drawing.Rectangle(RECT value) => new global::System.Drawing.Rectangle(value.left, value.top, value.right, value.bottom);
	public static implicit operator RECT(global::System.Drawing.Rectangle value) => new RECT(value);
}
