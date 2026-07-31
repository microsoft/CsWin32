using System.Diagnostics.CodeAnalysis;
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
    static async Task Main(string[] args)
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

        shellItem.BindToHandler<object>(null, bhidStorageItem, out object storageObject);
        Ensure(storageObject is IStorageItem objectStorageItem
            && string.Equals(objectStorageItem.Name, "win.ini", StringComparison.OrdinalIgnoreCase));

        shellItem.BindToHandler<IStream>(null, bhidStream, out IStream stream);
        byte[] buffer = new byte[8];
        stream.Read(buffer, out uint bytesRead);
        Ensure(bytesRead > 0);

        StorageFile managedStorageFile = await StorageFile.GetFileFromPathAsync(filePath);
        VerifyManagedImplementer<IStorageItem>(
            managedStorageFile,
            WinRT.GuidGenerator.CreateIID(typeof(IStorageItem)),
            bhidStorageItem);
        VerifyManagedImplementer<IStream>(stream, typeof(IStream).GUID, bhidStream);

        PInvoke.CreateBindCtx(0, out IBindCtx bindContext).ThrowOnFailure();
        VerifyManagedImplementer<IBindCtx>(bindContext, typeof(IBindCtx).GUID, bhidStream);
    }

    private static void Ensure(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException();
        }
    }

    private static unsafe void VerifyManagedImplementer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(object returnedValue, Guid requestedIid, Guid bindHandler)
        where T : class
    {
        ManagedShellItem managed = new(returnedValue);
        StrategyBasedComWrappers comWrappers = new();
        nint ccw = comWrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
        object rcw = comWrappers.GetOrCreateObjectForComInstance(ccw, CreateObjectFlags.UniqueInstance);
        Marshal.Release(ccw);
        try
        {
            IShellItem proxy = (IShellItem)rcw;
            proxy.BindToHandler<T>(null, bindHandler, out T result);
            Ensure(result is not null);

            IShellItemRaw rawProxy = (IShellItemRaw)rcw;
            rawProxy.BindToHandler(null!, &bindHandler, in requestedIid, out nint rawResult);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(rawResult, in requestedIid, out nint queriedResult));
                try
                {
                    Ensure(rawResult == queriedResult);
                }
                finally
                {
                    Marshal.Release(queriedResult);
                }
            }
            finally
            {
                Marshal.Release(rawResult);
            }

            Ensure(managed.BindToHandlerCallCount == 2);
        }
        finally
        {
            ((ComObject)rcw).FinalRelease();
        }
    }
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItemRaw
{
    unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, in Guid riid, out nint ppv);
}

[GeneratedComClass]
internal partial class ManagedShellItem(object returnedValue) : IShellItem
{
    internal int BindToHandlerCallCount { get; private set; }

    public unsafe void BindToHandler(IBindCtx pbc, Guid* bhid, in Guid riid, out object ppv)
    {
        this.BindToHandlerCallCount++;
        ppv = returnedValue;
    }

    public void GetParent(out IShellItem ppsi) => throw new NotImplementedException();

    public unsafe void GetDisplayName(SIGDN sigdnName, PWSTR* ppszName) => throw new NotImplementedException();

    public unsafe void GetAttributes(SFGAO_FLAGS sfgaoMask, SFGAO_FLAGS* psfgaoAttribs) => throw new NotImplementedException();

    public void Compare(IShellItem psi, uint hint, out int piOrder) => throw new NotImplementedException();
}
