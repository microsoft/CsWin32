/// <summary>
/// Provides helpers for implementing COM methods with an IID and <c>[ComOutPtr]</c> parameter pair.
/// </summary>
internal static unsafe class ComOutPtr
{
	/// <summary>
	/// Gets a caller-owned pointer to the interface identified by <paramref name="iid"/> on a managed COM or Windows Runtime object.
	/// </summary>
	/// <param name="value">The managed object to expose, or <see langword="null"/> to return a null pointer.</param>
	/// <param name="iid">The identifier of the exact interface to return.</param>
	/// <returns>
	/// A pointer with one outstanding reference that ownership transfers to the native caller.
	/// The caller is responsible for releasing the pointer.
	/// </returns>
	/// <remarks>An exception is thrown when <paramref name="value"/> does not expose the requested interface.</remarks>
	internal static void* FromManaged(object value, in global::System.Guid iid)
	{
		if (value is null)
		{
			return null;
		}

		nint identity = GetIdentity(value);
		try
		{
			int hr = QueryInterface(identity, in iid, out nint requestedInterface);
			if (hr < 0)
			{
				if (requestedInterface != 0)
				{
					global::System.Runtime.InteropServices.Marshal.Release(requestedInterface);
				}

				global::System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
			}

			return (void*)requestedInterface;
		}
		finally
		{
			FreeIdentity(identity);
		}
	}

#if usesComSourceGenerators
	private static int QueryInterface(nint identity, in global::System.Guid iid, out nint requestedInterface) =>
		global::System.Runtime.InteropServices.Marshal.QueryInterface(identity, in iid, out requestedInterface);
#else
#pragma warning disable CS9191
	private static int QueryInterface(nint identity, in global::System.Guid iid, out nint requestedInterface)
	{
		global::System.Guid requestedIid = iid;
		return global::System.Runtime.InteropServices.Marshal.QueryInterface(identity, ref requestedIid, out requestedInterface);
	}
#pragma warning restore CS9191
#endif

#if usesAutoWinRTMarshalling
	private static nint GetIdentity(object value) => ComOrWinRTObjectMarshaller.ConvertToUnmanaged(value);

	private static void FreeIdentity(nint identity) => ComOrWinRTObjectMarshaller.Free(identity);
#else
#if usesComSourceGenerators
	private static nint GetIdentity(object value) =>
		(nint)global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<object>.ConvertToUnmanaged(value);

	private static void FreeIdentity(nint identity) =>
		global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<object>.Free((void*)identity);
#else
	private static nint GetIdentity(object value) =>
		global::System.Runtime.InteropServices.Marshal.GetIUnknownForObject(value);

	private static void FreeIdentity(nint identity) =>
		global::System.Runtime.InteropServices.Marshal.Release(identity);
#endif
#endif
}
