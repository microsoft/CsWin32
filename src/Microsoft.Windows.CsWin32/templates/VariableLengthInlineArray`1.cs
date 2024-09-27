internal struct VariableLengthInlineArray<T>
	where T : unmanaged
{
	internal T e0;

#if canUseUnscopedRef

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

#endif
}
