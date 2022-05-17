/// <summary>
/// This class wrapper is stripped so that individual helper methods may be injected into the generated PInvoke class.
/// </summary>
internal class PInvokeClassHelperMethods
{
	private static void EnsureNullTerminated(Span<char> buffer, string parameterName)
	{
		if (buffer.Length == 0 || buffer[buffer.Length - 1] != '\0')
		{
			for (int i = buffer.Length - 2; i >= 0; i--)
			{
				if (buffer[i] == '\0')
				{
					return;
				}
			}

			throw new ArgumentException("Required null terminator is missing.", parameterName);
		}
	}
}
