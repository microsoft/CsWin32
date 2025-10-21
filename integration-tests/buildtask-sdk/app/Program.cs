using Windows.Win32.Foundation;

class Program
{
    static void Main(string[] args)
    {
        Windows.Win32.PInvoke.GetTickCount();

        HWND hwnd = Windows.Win32.PInvoke.GetForegroundWindow();
    }
}
