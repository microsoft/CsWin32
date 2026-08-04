#if usesComSourceGenerators
[global::System.Runtime.InteropServices.Marshalling.CustomMarshaller(
	typeof(object),
	global::System.Runtime.InteropServices.Marshalling.MarshalMode.ManagedToUnmanagedOut,
	typeof(ComOrWinRTObjectMarshaller))]
[global::System.Runtime.InteropServices.Marshalling.CustomMarshaller(
	typeof(object),
	global::System.Runtime.InteropServices.Marshalling.MarshalMode.UnmanagedToManagedOut,
	typeof(ComOrWinRTObjectMarshaller))]
#endif
internal static unsafe class ComOrWinRTObjectMarshaller
{
	private const int E_NOINTERFACE = unchecked((int)0x80004002);

	private static readonly global::System.Guid IID_IUnknown = new global::System.Guid(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
	private static readonly global::System.Guid IID_IInspectable = new global::System.Guid(0xAF86E2E0, 0xB12D, 0x4C6A, 0x9C, 0x5A, 0xD7, 0xAA, 0x65, 0x10, 0x1E, 0x90);

	/// <summary>Gets the interface identifier to request for a friendly generic COM output.</summary>
	internal static global::System.Guid GetIID<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>()
		where T : class
	{
		if (typeof(T) == typeof(object))
		{
			return IID_IUnknown;
		}

		return global::WinRT.Projections.IsTypeWindowsRuntimeType(typeof(T))
			? global::WinRT.GuidGenerator.CreateIID(typeof(T))
			: typeof(T).GUID;
	}

#if usesComSourceGenerators
	/// <summary>Projects a native COM identity as a Windows Runtime object when it implements <c>IInspectable</c>.</summary>
	public static object ConvertToManaged(nint value)
	{
		if (value == 0)
		{
			return null;
		}

		global::System.Guid iid = IID_IInspectable;
		int hr = global::System.Runtime.InteropServices.Marshal.QueryInterface(value, in iid, out nint inspectable);
		if (hr >= 0)
		{
			try
			{
				return global::WinRT.MarshalInspectable<object>.FromAbi(inspectable);
			}
			finally
			{
				global::System.Runtime.InteropServices.Marshal.Release(inspectable);
			}
		}

		if (hr != E_NOINTERFACE)
		{
			global::System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
		}

		return global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<object>.ConvertToManaged((void*)value);
	}

	/// <summary>Converts either a COM or Windows Runtime managed object to its COM identity.</summary>
	public static nint ConvertToUnmanaged(object value)
	{
		if (value is null)
		{
			return 0;
		}

		return (nint)global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<object>
			.ConvertToUnmanaged(value);
	}

	/// <summary>Releases the ABI identity pointer produced for or received from source-generated interop.</summary>
	public static void Free(nint value) =>
		global::System.Runtime.InteropServices.Marshalling.ComInterfaceMarshaller<object>.Free((void*)value);
#else
	/// <summary>
	/// Reprojects a built-in COM wrapper through C#/WinRT when the native identity implements <c>IInspectable</c>.
	/// </summary>
	internal static object ConvertToManaged(object value)
	{
		if (value is null)
		{
			return null;
		}

		nint identity = global::System.Runtime.InteropServices.Marshal.GetIUnknownForObject(value);
		try
		{
			global::System.Guid iid = IID_IInspectable;
			int hr = global::System.Runtime.InteropServices.Marshal.QueryInterface(identity, in iid, out nint inspectable);
			if (hr >= 0)
			{
				try
				{
					return global::WinRT.MarshalInspectable<object>.FromAbi(inspectable);
				}
				finally
				{
					global::System.Runtime.InteropServices.Marshal.Release(inspectable);
				}
			}

			if (hr != E_NOINTERFACE)
			{
				global::System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
			}

			return value;
		}
		finally
		{
			global::System.Runtime.InteropServices.Marshal.Release(identity);
		}
	}
#endif
}
