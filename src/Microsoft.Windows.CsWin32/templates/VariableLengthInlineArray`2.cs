internal struct VariableLengthInlineArray<T, TBlittable>
	where T : unmanaged
	where TBlittable : unmanaged
{
#if canUseUnscopedRef
	private TBlittable _e0;

	[UnscopedRef]
	internal ref T e0 => ref Unsafe.As<TBlittable, T>(ref this._e0);

#if canUseUnsafeAdd
	internal ref T this[int index]
	{
		[UnscopedRef]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => ref Unsafe.Add(ref this.e0, index);
	}
#endif

#if canUseSpan
	[UnscopedRef]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal Span<T> AsSpan(int length)
	{
#if canCallCreateSpan
		return MemoryMarshal.CreateSpan(ref this.e0, length);
#else
		unsafe
		{
			fixed (void* p = &this.e0)
			{
				return new Span<T>(p, length);
			}
		}
#endif
	}
#endif

#else
	internal TBlittable e0;
#endif
}
