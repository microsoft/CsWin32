/// <summary>
/// This class wrapper is stripped so that individual helper methods may be injected into the generated PInvoke class.
/// </summary>
internal class PInvokeClassHelperMethods
{
	private static void EnsureNullTerminated(Span<char> buffer, string parameterName)
	{
		if (buffer != null && buffer.LastIndexOf('\0') == -1)
		{
			throw new ArgumentException("Required null terminator is missing.", parameterName);
		}
	}
}
