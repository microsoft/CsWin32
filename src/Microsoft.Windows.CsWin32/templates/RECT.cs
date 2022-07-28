partial struct RECT
{
	internal RECT(global::System.Drawing.Rectangle value) :
		this(value.Left, value.Top, value.Right, value.Bottom) { }
	internal RECT(global::System.Drawing.Point location, global::System.Drawing.Size size) :
		this(location.X, location.Y, unchecked(location.X + size.Width), unchecked(location.Y + size.Height)) { }
	internal RECT(int left, int top, int right, int bottom)
	{
		this.left = left;
		this.top = top;
		this.right = right;
		this.bottom = bottom;
	}

	public static RECT FromXYWH(int x, int y, int width, int height) =>
		new RECT(x, y, unchecked(x + width), unchecked(y + height));
	public readonly int Width => unchecked(this.right - this.left);
	public readonly int Height => unchecked(this.bottom - this.top);
	public readonly bool IsEmpty => this.left == 0 && this.top == 0 && this.right == 0 && this.bottom == 0;
	public readonly int X => this.left;
	public readonly int Y => this.top;
	public readonly global::System.Drawing.Size Size => new global::System.Drawing.Size(this.Width, this.Height);
	public static implicit operator global::System.Drawing.Rectangle(RECT value) => new global::System.Drawing.Rectangle(value.left, value.top, value.right, value.bottom);
	public static implicit operator global::System.Drawing.RectangleF(RECT value) => new global::System.Drawing.RectangleF(value.left, value.top, value.right, value.bottom);
	public static implicit operator RECT(global::System.Drawing.Rectangle value) => new RECT(value);
}
