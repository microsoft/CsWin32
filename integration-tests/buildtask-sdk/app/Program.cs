using System;
using System.IO;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

class Program
{
    static void Main(string[] args)
    {
        PInvoke.GetTickCount();

        HWND hwnd = PInvoke.GetForegroundWindow();

        if (args.Length == 0 || args[0] != "--validate-com-out-ptr")
        {
            return;
        }

        Guid bhidStorageItem = new("404e2109-77d2-4699-a5a0-4fdf10db9837");
        string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");

        PInvoke.SHCreateItemFromParsingName<IShellItem>(filePath, null, out IShellItem shellItem).ThrowOnFailure();

        shellItem.BindToHandler<IStorageItem>(null, bhidStorageItem, out IStorageItem storageItem);
        Ensure(string.Equals(storageItem.Name, "win.ini", StringComparison.OrdinalIgnoreCase));

        shellItem.BindToHandler<object>(null, bhidStorageItem, out object storageObject);
        Ensure(storageObject is IStorageItem objectStorageItem
            && string.Equals(objectStorageItem.Name, "win.ini", StringComparison.OrdinalIgnoreCase));
    }

    private static void Ensure(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException();
        }
    }
}
