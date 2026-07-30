internal static unsafe class ComOutPtrHelpers
{
	private static readonly global::System.Guid IID_IUnknown = new global::System.Guid(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

#if canUseCsWinRT
	private static readonly global::System.Guid IID_IInspectable = new global::System.Guid(0xAF86E2E0, 0xB12D, 0x4C6A, 0x9C, 0x5A, 0xD7, 0xAA, 0x65, 0x10, 0x1E, 0x90);
#endif

	/// <summary>
	/// Validates <paramref name="marshalling"/> against <typeparamref name="T"/> and replaces
	/// <c>ComOutPtrMarshalling.Default</c> with the policy implied by <typeparamref name="T"/>.
	/// </summary>
	/// <typeparam name="T">The type the caller wants to receive. Must be an interface or <c>object</c>.</typeparam>
	/// <param name="marshalling">The requested policy.</param>
	/// <returns>A policy other than <c>ComOutPtrMarshalling.Default</c>.</returns>
	/// <remarks>
	/// Throws <c>NotSupportedException</c> when <typeparamref name="T"/> can never receive a COM output pointer, or when
	/// C#/WinRT is required but not referenced. Throws <c>ArgumentException</c> when <paramref name="marshalling"/> cannot
	/// produce a value assignable to <typeparamref name="T"/>.
	/// </remarks>
	internal static winmdroot.ComOutPtrMarshalling Resolve<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(winmdroot.ComOutPtrMarshalling marshalling)
		where T : class
	{
		if (!Classification<T>.IsSupported)
		{
			throw new global::System.NotSupportedException($"'{typeof(T)}' cannot receive a COM output pointer. Use an interface type or 'object'.");
		}

		switch (marshalling)
		{
			case winmdroot.ComOutPtrMarshalling.Default:
				return Classification<T>.Default;

			case winmdroot.ComOutPtrMarshalling.ComObject:
			case winmdroot.ComOutPtrMarshalling.ComObjectUniqueInstance:
				if (Classification<T>.IsWindowsRuntime)
				{
					throw new global::System.ArgumentException($"'{typeof(T)}' is a Windows Runtime type, which cannot be projected as a COM object wrapper.", nameof(marshalling));
				}

				return marshalling;

			case winmdroot.ComOutPtrMarshalling.WindowsRuntime:
#if canUseCsWinRT
				if (!Classification<T>.IsObject && !Classification<T>.IsWindowsRuntime)
				{
					throw new global::System.ArgumentException($"'{typeof(T)}' is not a Windows Runtime type. Request 'object' to obtain a Windows Runtime wrapper that this projection cannot name.", nameof(marshalling));
				}

				return marshalling;
#else
				throw new global::System.NotSupportedException("ComOutPtrMarshalling.WindowsRuntime requires a reference to C#/WinRT.");
#endif

			default:
				throw new global::System.ArgumentOutOfRangeException(nameof(marshalling));
		}
	}

	/// <summary>Gets the interface identifier to request from native code.</summary>
	/// <typeparam name="T">The type the caller wants to receive.</typeparam>
	/// <param name="resolved">A policy previously returned from <c>Resolve</c>.</param>
	/// <returns>The IID to pass to native code.</returns>
	internal static global::System.Guid GetIID<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(winmdroot.ComOutPtrMarshalling resolved)
		where T : class
	{
#if canUseCsWinRT
		if (resolved == winmdroot.ComOutPtrMarshalling.WindowsRuntime)
		{
			return Classification<T>.IsObject ? IID_IInspectable : WindowsRuntimeIID<T>.Value;
		}
#endif

		return Classification<T>.IsObject ? IID_IUnknown : typeof(T).GUID;
	}

	/// <summary>Projects a raw COM pointer into a managed wrapper without consuming the reference it carries.</summary>
	/// <typeparam name="T">The type the caller wants to receive.</typeparam>
	/// <param name="value">The pointer produced by native code, which may be 0.</param>
	/// <param name="resolved">A policy previously returned from <c>Resolve</c>.</param>
	/// <returns>The managed wrapper, or <see langword="null"/> when <paramref name="value"/> is 0.</returns>
	internal static T ConvertToManaged<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(nint value, winmdroot.ComOutPtrMarshalling resolved)
		where T : class
	{
		if (value == 0)
		{
			return null!;
		}

		switch (resolved)
		{
			case winmdroot.ComOutPtrMarshalling.ComObjectUniqueInstance:
				return global::System.Runtime.InteropServices.Marshalling.UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)value)!;

#if canUseCsWinRT
			case winmdroot.ComOutPtrMarshalling.WindowsRuntime:
				return Classification<T>.IsObject ? (T)global::WinRT.MarshalInspectable<object>.FromAbi(value) : global::WinRT.MarshalInterface<T>.FromAbi(value);
#endif

			default:
				return global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<T>.ConvertToManaged((void*)value)!;
		}
	}

	/// <summary>Releases the reference carried by a raw COM pointer produced by native code.</summary>
	/// <typeparam name="T">The type the caller wanted to receive.</typeparam>
	/// <param name="value">The pointer produced by native code, which may be 0.</param>
	/// <param name="resolved">A policy previously returned from <c>Resolve</c>.</param>
	internal static void Free<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(nint value, winmdroot.ComOutPtrMarshalling resolved)
		where T : class
	{
		if (value == 0)
		{
			return;
		}

		switch (resolved)
		{
			case winmdroot.ComOutPtrMarshalling.ComObjectUniqueInstance:
				global::System.Runtime.InteropServices.Marshalling.UniqueComInterfaceMarshaller<T>.Free((void*)value);
				break;

#if canUseCsWinRT
			case winmdroot.ComOutPtrMarshalling.WindowsRuntime:
				if (Classification<T>.IsObject)
				{
					global::WinRT.MarshalInspectable<object>.DisposeAbi(value);
				}
				else
				{
					global::WinRT.MarshalInterface<T>.DisposeAbi(value);
				}

				break;
#endif

			default:
				global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<T>.Free((void*)value);
				break;
		}
	}

	/// <summary>
	/// Gets the raw companion interface used to invoke a policy-bearing COM method.
	/// </summary>
	/// <typeparam name="TPublic">The public generated COM interface implemented by <paramref name="value"/>.</typeparam>
	/// <typeparam name="TRaw">The same-IID raw companion interface.</typeparam>
	/// <param name="value">The receiver of the friendly overload.</param>
	/// <param name="publicInterface">
	/// Receives the temporary public-interface pointer when <paramref name="value"/> is a direct managed implementation,
	/// or 0 when it is already projected as <typeparamref name="TRaw"/>.
	/// </param>
	/// <returns>The raw companion interface to invoke.</returns>
	internal static TRaw GetRawInterface<TPublic, TRaw>(TPublic value, out nint publicInterface)
		where TPublic : class
		where TRaw : class
	{
		if (value is TRaw raw)
		{
			publicInterface = 0;
			return raw;
		}

		void* valueAbi = global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<TPublic>.ConvertToUnmanaged(value);
		try
		{
			TRaw result = global::System.Runtime.InteropServices.Marshalling.UniqueComInterfaceMarshaller<TRaw>.ConvertToManaged(valueAbi)!;
			publicInterface = (nint)valueAbi;
			return result;
		}
		catch
		{
			global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<TPublic>.Free(valueAbi);
			throw;
		}
	}

	/// <summary>Releases a raw companion adapter created for a direct managed implementation.</summary>
	/// <typeparam name="TPublic">The public generated COM interface.</typeparam>
	/// <typeparam name="TRaw">The same-IID raw companion interface.</typeparam>
	/// <param name="value">The raw companion returned from <see cref="GetRawInterface{TPublic, TRaw}(TPublic, out nint)"/>.</param>
	/// <param name="publicInterface">The temporary public-interface pointer, or 0 when no adapter was required.</param>
	internal static void FreeRawInterface<TPublic, TRaw>(TRaw value, nint publicInterface)
		where TPublic : class
		where TRaw : class
	{
		if (publicInterface == 0)
		{
			return;
		}

		try
		{
			((global::System.Runtime.InteropServices.Marshalling.ComObject)(object)value).FinalRelease();
		}
		finally
		{
			global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<TPublic>.Free((void*)publicInterface);
		}
	}

	/// <summary>Classifies a closed generic type argument once per instantiation.</summary>
	/// <typeparam name="T">The type the caller wants to receive.</typeparam>
	private static class Classification<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>
		where T : class
	{
		internal static readonly bool IsObject = typeof(T) == typeof(object);

#if canUseCsWinRT
		internal static readonly bool IsWindowsRuntime = !IsObject && global::WinRT.Projections.IsTypeWindowsRuntimeType(typeof(T));
#else
		internal static readonly bool IsWindowsRuntime = false;
#endif

		internal static readonly bool IsSupported = IsObject || typeof(T).IsInterface;

		internal static readonly winmdroot.ComOutPtrMarshalling Default = IsWindowsRuntime ? winmdroot.ComOutPtrMarshalling.WindowsRuntime : winmdroot.ComOutPtrMarshalling.ComObject;
	}

#if canUseCsWinRT
	/// <summary>Caches the C#/WinRT interface identifier for a closed generic type argument.</summary>
	/// <typeparam name="T">A projected Windows Runtime interface.</typeparam>
	private static class WindowsRuntimeIID<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>
	{
		internal static readonly global::System.Guid Value = global::WinRT.GuidGenerator.CreateIID(typeof(T));
	}
#endif
}
