// This test verifies that the Roslyn 5.0 analyzer leg is loaded and the
// extensionReceiver feature works. The base project generates PInvoke with
// GetTickCount. This app project uses extensionReceiver to attach
// GetForegroundWindow as an extension member of the base PInvoke class.
//
// If the Roslyn 4.11 leg were loaded instead, PInvoke013 would reject
// extensionReceiver, GetForegroundWindow would land on AppPInvokes, and
// the PInvoke.GetForegroundWindow() call below would fail with CS0117.
using Windows.Win32;
using Windows.Win32.Foundation;

class Program
{
    static void Main(string[] args)
    {
        // From the base project (normal generation)
        PInvoke.GetTickCount();

        // From this project via extensionReceiver (C# 14 extension member)
        HWND hwnd = PInvoke.GetForegroundWindow();
    }
}
