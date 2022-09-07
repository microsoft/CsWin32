namespace Windows.Win32.CsWin32.InteropServices
{
	internal class WinRTCustomMarshaler : global::System.Runtime.InteropServices.ICustomMarshaler
	{
		private string winrtClassName;
		private bool lookedForFromAbi;
		private global::System.Reflection.MethodInfo fromAbi;

		private WinRTCustomMarshaler(string cookie)
		{
			this.winrtClassName = cookie;
		}

		/// <summary>
		/// Gets an instance of the marshaler given a cookie
		/// </summary>
		/// <param name="cookie">Cookie used to create marshaler</param>
		/// <returns>Marshaler</returns>
		public static global::System.Runtime.InteropServices.ICustomMarshaler GetInstance(string cookie)
		{
			return new WinRTCustomMarshaler(cookie);
		}

		void global::System.Runtime.InteropServices.ICustomMarshaler.CleanUpManagedData(object ManagedObj)
		{
		}

		void global::System.Runtime.InteropServices.ICustomMarshaler.CleanUpNativeData(global::System.IntPtr pNativeData)
		{
			global::System.Runtime.InteropServices.Marshal.Release(pNativeData);
		}

		int global::System.Runtime.InteropServices.ICustomMarshaler.GetNativeDataSize()
		{
			throw new global::System.NotImplementedException();
		}

		global::System.IntPtr global::System.Runtime.InteropServices.ICustomMarshaler.MarshalManagedToNative(object ManagedObj)
		{
			throw new global::System.NotImplementedException();
		}

		object global::System.Runtime.InteropServices.ICustomMarshaler.MarshalNativeToManaged(global::System.IntPtr pNativeData)
		{
			if (!this.lookedForFromAbi)
			{
				var assembly = typeof(global::Windows.Foundation.IMemoryBuffer).Assembly;
				var type = global::System.Type.GetType($"{this.winrtClassName}, {assembly.FullName}");

				this.fromAbi = type.GetMethod("FromAbi");
				this.lookedForFromAbi = true;
			}

			if (this.fromAbi != null)
			{
				return this.fromAbi.Invoke(null, new object[] { pNativeData });
			}
			else
			{
				return global::System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(pNativeData);
			}
		}
	}
}
