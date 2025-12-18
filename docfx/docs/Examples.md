# Code examples

When, for example, marshaling is enabled and `useSafeHandles` is `true`, the code can be different than that in C++. Here we show a few code examples of using CsWin32.

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

## Use PNP APIs (shows omitted optional params and cbSize-d struct)

This sample shows how to call an API where we've omitted some optional params -- note that we must
use named parameters when passing parameters past the omitted optional ones. This also shows how to
use Span APIs, and in this case one where we first call the API to get the buffer size, create the buffer
and then call again to populate the buffer.

```
SetupDiGetClassDevs
SetupDiEnumDeviceInfo
SetupDiGetDeviceInstanceId
```

```cs
using SafeHandle hDevInfo = PInvoke.SetupDiGetClassDevs(
    Flags: SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);

var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };

uint index = 0;
while (PInvoke.SetupDiEnumDeviceInfo(hDevInfo, index++, ref devInfo))
{
    PInvoke.SetupDiGetDeviceInstanceId(hDevInfo, in devInfo, RequiredSize: out uint requiredSize);

    Span<char> instanceIdSpan = new char[(int)requiredSize];
    PInvoke.SetupDiGetDeviceInstanceId(hDevInfo, in devInfo, instanceIdSpan);

    this.outputHelper.WriteLine($"Device {devInfo.ClassGuid} Instance ID: {instanceIdSpan.ToString()}");
}
```

## Pass struct as a Span<byte>

In this short example, we see how to pass a struct to a method that accepts a `Span<byte>`. `new Span<SHFILEINFOW>(ref fileInfo)` lets us
get a `Span<SHFILEINFOW>` and then `MemoryMarshal.AsBytes` reinterprets that same Span as a `Span<byte>` with the expected size. The cswin32
method will pass the Span's length to the native method as the "cb" count bytes parameter.

```cs
SHFILEINFOW fileInfo = default;
PInvoke.SHGetFileInfo(
    "c:\\windows\\notepad.exe",
    FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
    MemoryMarshal.AsBytes(new Span<SHFILEINFOW>(ref fileInfo)),
    SHGFI_FLAGS.SHGFI_DISPLAYNAME);
```

## Omitting optional out/ref parameters

APIs with optional `in` parameters are tagged with `[Optional]` attribute and such parameters can be omitted, but APIs
with optional `out` or `ref` parameters must always be passed. When the native method has these as `[optional]` and you need
to pass _some_ but not all of those parameters, you can pass "null" to the native method using `ref Unsafe.NullRef<T>()`.

This sample also shows passing `null` for SafeHandle-typed parameters which are not optional per the SDK headers but the
implementation allows for them to be null.

This sample shows a number of advanced COM marshalling scenarios

### Marshalling enabled (COM wrappers, AOT compatible)

```cs
// CoCreateInstance CLSID_WbemLocator
IWbemLocator locator = WbemLocator.CreateInstance<IWbemLocator>();

var ns = new SysFreeStringSafeHandle(Marshal.StringToBSTR(@"ROOT\Microsoft\Windows\Defender"), true);
locator.ConnectServer(ns, new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), new SysFreeStringSafeHandle(), 0, new SafeFileHandle(), null, out IWbemServices services);

unsafe
{
    PInvoke.CoSetProxyBlanket(
            services,
            10, // RPC_C_AUTHN_WINNT is 10
            0,  // RPC_C_AUTHZ_NONE is 0
            pServerPrincName: null,
            dwAuthnLevel: RPC_C_AUTHN_LEVEL.RPC_C_AUTHN_LEVEL_CALL,
            dwImpLevel: RPC_C_IMP_LEVEL.RPC_C_IMP_LEVEL_IMPERSONATE,
            pAuthInfo: null,
            dwCapabilities: EOLE_AUTHENTICATION_CAPABILITIES.EOAC_NONE);
}

var className = new SysFreeStringSafeHandle(Marshal.StringToBSTR("MSFT_MpScan"), true);
IWbemClassObject? classObj = null; // out param

services.GetObject(className, WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_RETURN_WBEM_COMPLETE, null, ref classObj, ref Unsafe.NullRef<IWbemCallResult>());

classObj.GetMethod("Start", 0, out IWbemClassObject pInParamsSignature, out IWbemClassObject ppOutSignature);

```
