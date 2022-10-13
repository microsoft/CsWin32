internal partial struct DECIMAL
{
	public DECIMAL(decimal value)
	{
		unchecked
		{
			const int SignMask = (int)0x80000000;
#if NET5_0_OR_GREATER
			Span<int> bits = stackalloc int[4];
			decimal.GetBits(value, bits);
#else
			int[] bits = decimal.GetBits(value);
#endif
			uint lo32 = (uint)bits[0];
			uint mid32 = (uint)bits[1];
			uint hi32 = (uint)bits[2];
			byte scale = (byte)(bits[3] >> 16);
			byte sign = (bits[3] & SignMask) == SignMask ? (byte)0x80 : (byte)0x00;
			this.Anonymous2 = new _Anonymous2_e__Union() { Anonymous = new _Anonymous2_e__Union._Anonymous_e__Struct() { Lo32 = lo32, Mid32 = mid32 } };
			this.Hi32 = hi32;
			this.Anonymous1 = new _Anonymous1_e__Union() { Anonymous = new _Anonymous1_e__Union._Anonymous_e__Struct() { scale = scale, sign = sign } };
			this.wReserved = 0;
		}
	}

	public static implicit operator decimal(DECIMAL value)
	{
		return new decimal(
			(int)value.Anonymous2.Anonymous.Lo32,
			(int)value.Anonymous2.Anonymous.Mid32,
			(int)value.Hi32,
			value.Anonymous1.Anonymous.sign == 0x80,
			value.Anonymous1.Anonymous.scale);
	}

#if NET5_0_OR_GREATER
	public static implicit operator DECIMAL(decimal value) => new DECIMAL(value);
#endif
}
