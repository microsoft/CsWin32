# Code examples

## Retrieving the last-write time

Based on: [Retrieving the Last-Write Time](https://learn.microsoft.com/en-us/windows/win32/sysinfo/retrieving-the-last-write-time)

```
CreateFile
GetFileTime
FileTimeToSystemTime
SystemTimeToTzSpecificLocalTime
GENERIC_ACCESS_RIGHTS
INVALID_HANDLE_VALUE
MAX_PATH
```

```cs
static void Main(string[] args)
{
    Span<char> szBuf = stackalloc char[(int)PInvoke.MAX_PATH];

    if (args.Length is not 1)
    {
        Console.WriteLine("This sample takes a file name as a parameter\n");
        return;
    }

    using SafeHandle hFile = PInvoke.CreateFile(
        args[0],
        (uint)GENERIC_ACCESS_RIGHTS.GENERIC_READ,
        FILE_SHARE_MODE.FILE_SHARE_READ,
        null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null);

    if (hFile.IsInvalid)
    {
        Console.WriteLine("CreateFile failed with {0}\n", Marshal.GetLastPInvokeError());
        return;
    }

    if (GetLastWriteTime(hFile, szBuf))
        Console.WriteLine("Last write time is: {0}\n", szBuf.ToString());
}

static unsafe bool GetLastWriteTime(SafeHandle hFile, Span<char> lpszString)
{
    FILETIME ftCreate, ftAccess, ftWrite;
    SYSTEMTIME stUTC, stLocal;

    // Retrieve the file times for the file.
    if (!PInvoke.GetFileTime(hFile, out ftCreate, out ftAccess, out ftWrite))
        return false;

    // Convert the last-write time to local time.
    PInvoke.FileTimeToSystemTime(in ftWrite, out stUTC);
    PInvoke.SystemTimeToTzSpecificLocalTime(null, stUTC, out stLocal);

    // Build a string showing the date and time.
    string.Format("{0:00}/{1:00}/{2}  {3:00}:{4:00}", stLocal.wMonth, stLocal.wDay, stLocal.wYear, stLocal.wHour, stLocal.wMinute)
        .AsSpan().CopyTo(lpszString);

    return true;
}
```

## Retrieving information about all of the display monitors

```
EnumDisplayMonitors
GetMonitorInfo
MONITORINFOEXW
```

### Marshaling enabled

```cs
static Dictionary<string, RECT> _monitors = [];
static unsafe MONITORENUMPROC proc = new(MonitorEnumProc); // Keep this alive

static void Main(string[] args)
{
    BOOL fRes = PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, proc, 0);
    if (!fRes) return;

    foreach (var monitor in _monitors)
        Console.WriteLine($"Device: {monitor.Key}, Rect: ({monitor.Value.Width},{monitor.Value.Height})");
}

static unsafe BOOL MonitorEnumProc(HMONITOR hMonitor, HDC hdcMonitor, RECT* lprcMonitor, LPARAM dwData)
{
    MONITORINFOEXW info = default;
    info.monitorInfo.cbSize = (uint)sizeof(MONITORINFO);

    PInvoke.GetMonitorInfo(hMonitor, (MONITORINFO*)&info);

    _monitors.Add(info.szDevice.ToString(), *lprcMonitor);

    return true;
}
```

### Marshaling disabled (AOT-compatible)

```cs
static unsafe void Main(string[] args)
{
    BOOL fRes = PInvoke.EnumDisplayMonitors(HDC.Null, null, &MonitorEnumProc, 0);

    // ...
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
static unsafe BOOL MonitorEnumProc(HMONITOR hMonitor, HDC hdcMonitor, RECT* lprcMonitor, LPARAM dwData)
{
    // ...
}
```

## Use of SHCreateItemFromParsingName

```
IShellItem
SHCreateItemFromParsingName
```

### Marshaling enabled (built-in COM Interop, not AOT-compatible)

```cs
[STAThread]
static void Main(string[] args)
{
    HRESULT hr = PInvoke.SHCreateItemFromParsingName("C:\\Users\\you\\file.txt", null, typeof(IShellItem).GUID, out var ppv);
    if (hr.Failed) return;

    var shellItem = (IShellItem)ppv;

    Marshal.ReleaseComObject(shellItem);
}
```

### Marshaling enabled (Com Wrappers, AOT-compatible)

```cs
[STAThread]
static unsafe void Main(string[] args)
{
    HRESULT hr = PInvoke.SHCreateItemFromParsingName("C:\\Users\\you\\file.txt", null, typeof(IShellItem).GUID, out var ppv);
    if (hr.Failed) return;

    var shellItem = (IShellItem)ppv;

    // Let the GC to release shellItem
}
```

### Marshaling disabled (AOT-compatible)

```cs
[STAThread]
static unsafe void Main(string[] args)
{
    HRESULT hr;

    var IID_IShellItem = typeof(IShellItem).GUID;
    IShellItem* psi = null;

    fixed (char* pszPath = "C:\\Users\\you\\file.txt")
        hr = PInvoke.SHCreateItemFromParsingName(pszPath, null, &IID_IShellItem, (void**)&psi);
    if (hr.Failed) return;

    psi->Release();
}
```

## Using COM classes (NetFwMgr, INetFwMgr)

In this example we see how to activate a COM class and call methods on it via its primary interface. When marshalling
is enabled, we can use C# cast to do the "QueryInterface" that you would have seen in a native C++ sample.

```
NetFwMgr
INetFwMgr
IEnumVARIANT
INetFwAuthorizedApplication
```

### Marshalling enabled (built-in COM Interop, not AOT-compatible)

```cs
var fwMgr = (INetFwMgr)new NetFwMgr();
var authorizedApplications = fwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications;
var aaObjects = new object[authorizedApplications.Count];
var applicationsEnum = (IEnumVARIANT)authorizedApplications._NewEnum;
applicationsEnum.Next((uint)authorizedApplications.Count, aaObjects, out uint fetched);
foreach (var aaObject in aaObjects)
{
    var app = (INetFwAuthorizedApplication)aaObject;
    Console.WriteLine("---");
    Console.WriteLine($"Name: {app.Name.ToString()}");
    Console.WriteLine($"Enabled: {(bool)app.Enabled}");
    Console.WriteLine($"Remote Addresses: {app.RemoteAddresses.ToString()}");
    Console.WriteLine($"Scope: {app.Scope}");
    Console.WriteLine($"Process Image Filename: {app.ProcessImageFileName.ToString()}");
    Console.WriteLine($"IP Version: {app.IpVersion}");
}
```

### Marshalling enabled (COM wrappers, AOT compatible)

Note that in COM wrappers mode, the generated interfaces have get_ methods instead of properties. Some parameters
are also ComVariant instead of object because source generated COM does not support as much automatic marshalling
as built-in COM does.

```cs
var fwMgr = NetFwMgr.CreateInstance<INetFwMgr>();
var authorizedApplications = fwMgr.get_LocalPolicy().get_CurrentProfile().get_AuthorizedApplications();
var aaObjects = new ComVariant[authorizedApplications.get_Count()];
var applicationsEnum = (IEnumVARIANT)authorizedApplications.get__NewEnum();
applicationsEnum.Next((uint)authorizedApplications.get_Count(), aaObjects, out uint fetched);
foreach (var aaObject in aaObjects)
{
    var app = (INetFwAuthorizedApplication)ComVariantMarshaller.ConvertToManaged(aaObject)!;

    Console.WriteLine("---");
    Console.WriteLine($"Name: {app.get_Name().ToString()}");
    Console.WriteLine($"Enabled: {(bool)app.get_Enabled()}");
    Console.WriteLine($"Remote Addresses: {app.get_RemoteAddresses().ToString()}");
    Console.WriteLine($"Scope: {app.get_Scope()}");
    Console.WriteLine($"Process Image Filename: {app.get_ProcessImageFileName().ToString()}");
    Console.WriteLine($"IP Version: {app.get_IpVersion()}");

    aaObject.Dispose();
}
```
