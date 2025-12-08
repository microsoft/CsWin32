# Code examples

When, for example, marshaling is enabled and `useSafeHandles` is `true`, the code can be different than that in C++. Here we show a few code examples of using CsWin32.

```json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "useSafeHandles": false,
  "comInterop": {
    "preserveSigMethods": ["*"]
  }
}
```

## Scenario 1: Retrieving the last-write time

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
    SafeHandle hFile;
    Span<char> szBuf = stackalloc char[(int)PInvoke.MAX_PATH];

    if (args.Length is not 1)
    {
        Console.WriteLine("This sample takes a file name as a parameter\n");
        return;
    }

    hFile = PInvoke.CreateFile(
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

    hFile.Close();
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

## Scenario 2: Retrieving information about all of the display monitors

```
EnumDisplayMonitors
GetMonitorInfo
MONITORINFOEXW
```

### Marshaling enabled

```cs
static Dictionary<string, RECT> _monitors = [];

static void Main(string[] args)
{
    BOOL fRes = PInvoke.EnumDisplayMonitors(HDC.Null, null, new(MonitorEnumProc), 0);
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

## Scenario 3: Use of SHCreateItemFromParsingName

```
IShellItem
SHCreateItemFromParsingName
```

### Marshaling enabled (built-in COM Interop, not AOT-compatible)

```cs
[STAThread]
static void Main(string[] args)
{
    var IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    HRESULT hr = PInvoke.SHCreateItemFromParsingName("C:\\Users\\you\\file.txt", null, in IID_IShellItem, out var ppv);
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
    var IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    HRESULT hr = PInvoke.SHCreateItemFromParsingName("C:\\Users\\you\\file.txt", null, in IID_IShellItem, out var ppv);
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

    var IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    IShellItem* psi = null;

    fixed (char* pszPath = "C:\\Users\\you\\file.txt")
        hr = PInvoke.SHCreateItemFromParsingName(pszPath, null, &IID_IShellItem, (void**)psi);
    if (hr.Failed) return;

    psi->Release();
}
```
