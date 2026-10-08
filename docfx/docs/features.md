# Features

- Generates interop code quickly at compilation time.
- Generates friendly overloads/extensions (including `SafeHandle`-types support).
- Generates xml documentation based on and links back to learn.microsoft.com
- Ships no bulky assemblies alongside your application.
- [Layered composition](composition.md): multiple assemblies can extend a single shared `PInvoke` static class so callers reach every native API through one symbol.

![Animation demonstrating p/invoke code generation](../images/demo.gif)

## Owned outputs and scoped native values

`RAIIFree` and compatible `FreeWith` annotations on return values and output parameters
tell CsWin32 which cleanup function owns that particular result. Friendly overloads
can return a SafeHandle for pointer-shaped allocations as well as native handles.
Unannotated pointers and the raw native signatures are unchanged.

CsWin32-generated SafeHandles expose a `DangerousValue` property with the original native type.
For example, `SHGetKnownFolderPath` supplies a `CoTaskMemFreePWSTRSafeHandle` whose
`DangerousValue` is `PWSTR`, although `CoTaskMemFree` accepts `void*`. Reading this
property does not acquire a reference to keep the resource alive. BCL wrappers such as
`SafeFileHandle` retain their existing APIs.

For an owner received from another component or as a parameter, acquire a scoped reference:

```csharp
using Windows.Win32;

static bool PathExists(CoTaskMemFreePWSTRSafeHandle path)
{
    using var lease = path.Lease();
    return PInvoke.PathFileExists(lease.Value);
}
```

`Lease()` pairs `DangerousAddRef` and `DangerousRelease` without an additional heap
allocation. Disposal through another alias cannot release the resource until the
lease ends. The lease's `Value` has the same native type as its owner. The lease
uses the `Dispose()` pattern; it does not require `IDisposable`.
Lease helpers require C# 9 or later.

An owner already held in a visible `using` scope can supply `owner.DangerousValue` directly.
This assumes ordinary exclusive ownership: another alias must not explicitly
dispose it during the call. A SafeHandle parameter alone is not such a lifetime
guarantee. Friendly input overloads accept base `SafeHandle` for types with existing
cleanup annotations and keep a reference alive throughout their calls. They also
accept `SafeHandle` for `Windows.Win32.Foundation.HANDLE` even without a type-level
cleanup annotation, preserving this behavior as ownership annotations move to outputs.
This heuristic does not apply to other types such as `HWND`, `PWSTR`, or arbitrary
pointers. Such resources can be passed using their owner's `DangerousValue` or a
lease's `Value`. Set `useSafeHandles` to `false` in `NativeMethods.json` to disable
SafeHandle projection.

The lifetime analyzer reports errors for leases that are not `using` locals, copies,
field storage, parameter passing, returns, and explicit disposal (`PInvoke015`).
It also reports errors for `DangerousValue` or a lease's `Value` without a visible
scope (`PInvoke016`), saving or returning a raw resource (`PInvoke017`), and releasing
an owned resource directly (`PInvoke018`). Use these accessors directly in calls,
or copy their contents into managed data such as a string. String interpolation
such as `$"Path: {path.DangerousValue}"` is allowed inside the owner's or lease's
scope. Interpolation into `FormattableString`
or `IFormattable` retains the raw arguments for later formatting and is not a string
copy. C# ref structs remain copyable: keep these diagnostics
enabled to enforce the supported no-copy convention.

Neither a lease nor this analyzer proves that an arbitrary native call does not
retain the resource, transfer ownership, or invalidate it independently. Such
APIs need their own explicit lifetime arrangement.

For cleanup requiring additional arguments, request a helper such as
`DeleteTimerQueueTimerSafeHandle` in `NativeMethods.txt`. It is abstract: derive
from it, keep the necessary cleanup context, and override `ReleaseHandle`.
Its typed `DangerousValue` is available inside that override. CsWin32 will not construct
this helper automatically or guess the additional cleanup arguments.
