using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "--validate-com-out-ptr")
        {
            return;
        }

        Guid bhidStorageItem = new("404e2109-77d2-4699-a5a0-4fdf10db9837");
        Guid bhidStream = new("1cebb3ab-7c10-499a-a417-92ca16c4cb83");
        string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");

        PInvoke.SHCreateItemFromParsingName<IShellItem>(filePath, null, out IShellItem shellItem).ThrowOnFailure();
        shellItem.BindToHandler<IStorageItem>(null, bhidStorageItem, out IStorageItem storageItem);
        Ensure(string.Equals(storageItem.Name, "win.ini", StringComparison.OrdinalIgnoreCase));

        ComOutPtrMarshalling resolved =
            ComOutPtrHelpers.Resolve<IReadOnlyList<string>>(ComOutPtrMarshalling.Default);
        Guid parameterizedIid = ComOutPtrHelpers.GetIID<IReadOnlyList<string>>(resolved);
        Ensure(resolved == ComOutPtrMarshalling.WindowsRuntime);
        Ensure(parameterizedIid == WinRT.GuidGenerator.CreateIID(typeof(IReadOnlyList<string>)));

        shellItem.BindToHandler<IStream>(
            null,
            bhidStream,
            out IStream unique,
            ComOutPtrMarshalling.ComObjectUniqueInstance);
        Ensure((object)unique is ComObject);
        ((ComObject)(object)unique).FinalRelease();

        var managed = new ManagedShellItem(shellItem);
        managed.BindToHandler<IShellItem>(null, bhidStream, out IShellItem managedResult);
        Ensure(managed.BindToHandlerCallCount == 1 && managedResult is not null);
    }

    private static void Ensure(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException();
        }
    }
}

[GeneratedComClass]
internal partial class ManagedShellItem(IShellItem inner) : IShellItem
{
    internal int BindToHandlerCallCount { get; private set; }

    public unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, Guid* riid, out object ppv)
    {
        this.BindToHandlerCallCount++;
        ppv = inner;
    }

    public void GetParent(out IShellItem ppsi) => throw new NotImplementedException();

    public unsafe void GetDisplayName(SIGDN sigdnName, PWSTR* ppszName) => throw new NotImplementedException();

    public unsafe void GetAttributes(SFGAO_FLAGS sfgaoMask, SFGAO_FLAGS* psfgaoAttribs) => throw new NotImplementedException();

    public void Compare(IShellItem psi, uint hint, out int piOrder) => throw new NotImplementedException();
}
