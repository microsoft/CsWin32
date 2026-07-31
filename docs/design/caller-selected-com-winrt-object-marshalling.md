# Caller-selected COM and WinRT object out-parameter marshalling

## Status

Not selected.

Related documents:

- [Adaptive COM and WinRT object out-parameter marshalling](adaptive-com-winrt-object-marshalling.md)
- [COM and WinRT object out-parameter marshalling options](com-winrt-object-marshalling-options.md)

This note records the caller-selected alternative that was evaluated. CsWin32 instead selected automatic runtime detection as described in the adaptive proposal.

## Summary

CsWin32 currently projects objects returned through COM `IID`/`void**` pairs with `ComInterfaceMarshaller<T>`. That produces the expected source-generated COM wrapper, but it fails when the caller needs a C#/WinRT projection such as `Windows.Storage.IStorageItem`.

This proposal adds a caller-visible policy:

```csharp
public enum ComOutPtrMarshalling
{
    Default,
    ComObject,
    WindowsRuntime,
}
```

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv,
    ComOutPtrMarshalling marshalling = ComOutPtrMarshalling.Default)
    where T : class;
```

`Default` uses the closed generic `T`:

- A projected C#/WinRT interface selects `WindowsRuntime`.
- `object` and a source-generated COM interface select `ComObject`.

The caller selects `WindowsRuntime` explicitly when `T` does not reveal the intent, including `out object` or a generated COM interface that must later expose WinRT interfaces.

`Default` and `ComObject` never probe the returned object merely because it might implement `IInspectable`. Ordinary COM calls keep their current wrapper behavior and do not pay a detection QI. Explicit `WindowsRuntime` with a generated COM `T` performs an intentional `QI(IInspectable)` because the caller requested a WinRT wrapper after requesting the object through a COM IID.

Applying the policy requires the raw output pointer. Flat P/Invokes can expose `out nint` directly. Source-generated COM methods require a same-IID raw companion because their public `out object` declaration cannot observe the friendly method's policy.

Unique COM ownership is not required by this proposal and is described as separate future work.

## Problem

A method following the `IID_PPV_ARGS` pattern returns an ABI interface pointer:

```csharp
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);
```

The current source-generated COM projection creates a `ComObject`. That wrapper cannot be cast to the C#/WinRT `IStorageItem` projection.

The generic type often communicates the desired wrapper:

- `IStorageItem` implies C#/WinRT.
- `IStream` implies source-generated COM.

It does not always do so:

- `object` may later be cast to either family.
- An object requested through a COM interface may later be cast to a WinRT interface.
- An inspectable object may still need to be represented primarily as COM.

Automatically querying every output for `IInspectable` solves the ambiguity, but changes the cost and wrapper selection of ordinary COM calls. This proposal keeps that choice at the call site.

## Goals

- Correctly project C#/WinRT interfaces returned through IID/output pairs.
- Preserve existing source-generated COM wrapper behavior and cost by default.
- Let callers explicitly choose COM or Windows Runtime projection.
- Infer the common choice from the closed generic `T`.
- Support `object` and generated COM `T` when the caller explicitly wants a WinRT wrapper.
- Keep managed `[GeneratedComInterface]` implementation methods object-shaped.
- Support Native AOT.
- Release every native reference on success and failure.

## Non-goals

- Probe every returned object for `IInspectable`.
- Infer a future WinRT cast after a value escapes as `object`.
- Select unique versus identity-cached COM ownership.
- Change input parameter marshalling.
- Apply the policy to every fixed-type COM output in the first implementation.
- Preserve exact source or binary signatures of generated projections.
- Solve sibling-`riid` correlation for arbitrary native callers of managed COM servers.

## Proposed API

### Generated enum

CsWin32 generates the policy enum when it emits a policy-bearing friendly method:

```csharp
namespace Windows.Win32;

public enum ComOutPtrMarshalling
{
    Default = 0,
    ComObject = 1,
    WindowsRuntime = 2,
}
```

The enum follows the configured visibility of generated APIs.

### Friendly method

The generated friendly method gains an optional trailing policy:

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv,
    ComOutPtrMarshalling marshalling = ComOutPtrMarshalling.Default)
    where T : class;
```

CsWin32 projections are primarily internal implementation details, so the exact existing signature does not need a forwarding compatibility overload.

The optional `Default` keeps the common call concise:

```csharp
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);
```

Ambiguous cases state the wrapper intent:

```csharp
shellItem.BindToHandler<object>(
    null,
    bhidStorageItem,
    out object storageItem,
    ComOutPtrMarshalling.WindowsRuntime);
```

```csharp
shellItem.BindToHandler<IStream>(
    null,
    bhidStream,
    out IStream stream,
    ComOutPtrMarshalling.ComObject);
```

## Policy semantics

| Policy | Supported `T` | Requested IID | Managed projection |
| --- | --- | --- | --- |
| `Default` | `object` or an interface | Selected from the type-directed policy | Selected from the type-directed policy |
| `ComObject` | `object` or a generated COM interface | `IID_IUnknown` for `object`; otherwise `typeof(T).GUID` | `ComInterfaceMarshaller<T>` |
| `WindowsRuntime` | `object`, a projected WinRT interface, or a generated COM interface | `IID_IInspectable` for `object`; WinRT IID for a projected WinRT interface; otherwise `typeof(T).GUID` | C#/WinRT wrapper, then cast to `T`; a generated COM `T` first queries the returned pointer for `IInspectable` |

Invalid or unsupported combinations fail before invoking native code:

- `ComObject` with a projected WinRT interface.
- A runtime class or other non-interface `T`, except for `object`.
- `WindowsRuntime` when C#/WinRT is not referenced.
- `WindowsRuntime` with a generated COM `T` when C#/WinRT dynamic interface casting is disabled.

Explicit `WindowsRuntime` with a generated COM `T` still has a runtime requirement: the object returned for the requested COM IID must implement `IInspectable`. `E_NOINTERFACE` from that projection QI is an error rather than a fallback to `ComObject`, because fallback would violate the selected policy.

Some APIs interpret the requested IID as part of the operation rather than a final QI. `out object` requests `IID_IUnknown` or `IID_IInspectable` and may not be accepted. Callers should use the semantic interface `T` when the API requires one.

### `Default`

The generated method classifies `typeof(T)` once per closed generic instantiation:

```csharp
WinRT.Projections.IsTypeWindowsRuntimeType(typeof(T))
```

The result is cached:

- Projected WinRT `T` resolves to `WindowsRuntime`.
- `object` resolves to `ComObject`.
- Generated COM `T` resolves to `ComObject`.

This is type-directed runtime classification, not source-generation-time specialization. It does not inspect the returned native object.

If the classifier cannot reliably cover every supported C#/WinRT type shape, an analyzer should detect calls with a statically known WinRT `T`, require explicit `WindowsRuntime`, and offer a code fix. The analyzer still cannot infer a later WinRT cast from `object`.

### Explicit `WindowsRuntime` with generated COM `T`

An inspectable object may also implement a source-generated COM interface. The caller may want the WinRT wrapper family while retaining immediate COM access:

```csharp
shellItem.BindToHandler<IStream>(
    null,
    bhidStream,
    out IStream stream,
    ComOutPtrMarshalling.WindowsRuntime);
```

The raw call requests `IID_IStream`, then queries the returned `IStream` pointer for `IID_IInspectable`. It projects that second pointer through C#/WinRT and casts the resulting wrapper to `IStream`.

On .NET 8 and later, C#/WinRT's `IWinRTObject` dynamic interface path recognizes source-generated COM interface metadata, queries the generated IID, and supplies its vtable. The adaptive prototype validated this behavior by invoking `IStream.Read` through a `WinRT.IInspectable` wrapper.

The original `IStream` output reference and the temporary `IInspectable` QI reference are released independently. This QI occurs only because the caller explicitly selected `WindowsRuntime`; it is not added to `Default` or `ComObject`.

This mode requires C#/WinRT dynamic interface casting to remain enabled. A generated guard should throw a targeted `NotSupportedException` when it is disabled.

## Raw ABI requirement

The policy must be applied before any managed wrapper is created.

Applying `ComInterfaceMarshaller<object>` first is insufficient:

- It creates the COM wrapper before `WindowsRuntime` can be selected.
- Converting that wrapper back to ABI adds work.
- It can leave an unnecessary wrapper in the COM identity cache.

The friendly method must receive the raw pointer, choose the projection, and release that pointer exactly once.

## Flat P/Invoke methods

Eligible flat methods expose the IID/output parameter as `out nint` in the generated interop declaration:

```csharp
[LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName")]
public static partial HRESULT SHCreateItemFromParsingName(
    string pszPath,
    IBindCtx? pbc,
    in Guid riid,
    out nint ppv);
```

The friendly method computes the IID, calls the raw declaration, applies the selected projection, and releases the returned reference in `finally`.

This changes the generated managed signature, not the native ABI. No duplicate P/Invoke entry point is required.

## COM interface impact

### Why the public interface cannot carry the policy

A generated COM interface method is not generic and its parameters must match the native vtable:

```csharp
[GeneratedComInterface]
public partial interface IShellItem
{
    void BindToHandler(
        IBindCtx? pbc,
        Guid* bhid,
        Guid* riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);
}
```

The method cannot receive the friendly method's managed-only policy. A custom marshaller on `out object` also cannot observe the caller's `T` or enum value.

The object-shaped method remains useful for managed implementers, so this proposal does not replace it with an unsafe output pointer.

### Same-IID raw companion

CsWin32 generates an internal interface with the same IID and vtable layout, but exposes policy-relevant outputs as `nint`:

```csharp
[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem__ComOutPtrRaw
{
    void BindToHandler(
        IBindCtx? pbc,
        Guid* bhid,
        Guid* riid,
        out nint ppv);
}
```

For an existing RCW, the friendly method dynamically casts the receiver to the raw companion, invokes the same COM slot, and projects the pointer according to the policy.

The companion must mirror the complete inherited and declared vtable layout through the target method. Generated managed classes must not implement both same-IID interfaces because that would place duplicate IID entries on one CCW.

### Direct managed implementations

A direct managed implementation of the public interface does not implement the private raw companion.

To keep the friendly method callable directly on that object, the generated extension:

1. Obtains the public interface's CCW pointer.
2. Projects a temporary unique RCW for the raw same-IID companion.
3. Invokes the raw slot.
4. Releases the temporary RCW and CCW references.

Existing RCWs stay on the direct raw-companion path.

This adapter is implementation complexity specific to the caller-selected design. The adaptive custom-marshaller design does not need it.

### Managed implementers

The managed method remains natural:

```csharp
public void BindToHandler(..., out object value)
{
    value = this.returnWinRT
        ? this.storageFile
        : this.comObject;
}
```

`ComInterfaceMarshaller<object>` already accepts C#/WinRT wrappers, source-generated COM wrappers, and managed generated-COM objects on the CCW side.

The raw caller then chooses `ComObject` or `WindowsRuntime` independently of what managed type the implementation assigned.

### Adjacent `riid` limitation

The object marshaller used by the managed CCW cannot see the sibling `riid`. It may return the object's identity pointer instead of a pointer already adjusted to the requested interface.

The generated friendly/raw-companion caller tolerates this because its projection performs the necessary QI. Arbitrary native clients that immediately dereference `ppv` as `riid` are not covered by this proposal unless the implementation independently guarantees the correct pointer.

This limitation is shared with the adaptive proposal and with the current object-shaped generated COM method.

## Projection and cleanup

The raw output carries one owned reference.

### COM

```csharp
T value = ComInterfaceMarshaller<T>.ConvertToManaged((void*)native);
ComInterfaceMarshaller<T>.Free((void*)native);
```

### Windows Runtime interface

```csharp
T value = WinRT.MarshalInterface<T>.FromAbi(native);
WinRT.MarshalInterface<T>.DisposeAbi(native);
```

### Windows Runtime wrapper with `object`

```csharp
object value = WinRT.MarshalInspectable<object>.FromAbi(native);
WinRT.MarshalInspectable<object>.DisposeAbi(native);
```

### Windows Runtime wrapper with generated COM `T`

The native call returns a pointer for the requested COM IID. That pointer is not itself an `IInspectable` pointer, even when the object also implements `IInspectable`:

```csharp
nint inspectable = 0;
T result;
try
{
    int hr = Marshal.QueryInterface(native, in IID_IInspectable, out inspectable);
    Marshal.ThrowExceptionForHR(hr);

    object value = WinRT.MarshalInspectable<object>.FromAbi(inspectable);
    result = (T)value;
}
finally
{
    if (inspectable != 0)
    {
        Marshal.Release(inspectable);
    }

    Marshal.Release(native);
}

ppv = result;
```

The generated implementation may use equivalent marshaller cleanup helpers. It must release both owned references, including when the QI, projection, or final cast fails.

Native AOT may use a generic `WinRT.IInspectable` wrapper instead of a concrete runtime-class wrapper. The requested interface remains the supported contract.

## Multiple IID/output pairs

An ECMA-335 metadata scan of `Microsoft.Windows.SDK.Win32Metadata` 71.0.14-preview and `Microsoft.Windows.WDK.Win32Metadata` 0.13.25-experimental, restricted to interface and P/Invoke methods that CsWin32 sends through friendly-overload generation, found:

- 420 generator-relevant methods with exactly one canonical adjacent IID/output pair.
- One method with two pairs: `ID3D12SwapChainAssistant.GetCurrentResourceAndCommandQueue`.
- 15 single-pair methods where the pair is not the final two parameters.

Each pair needs its own type parameter and policy:

```csharp
GetCurrentResourceAndCommandQueue<TResource, TQueue>(
    out TResource resource,
    out TQueue queue,
    ComOutPtrMarshalling resourceMarshalling = ComOutPtrMarshalling.Default,
    ComOutPtrMarshalling queueMarshalling = ComOutPtrMarshalling.Default);
```

Each output is projected and released independently.

The first implementation should remain scoped to IID/output generic methods rather than the broader set of fixed-type COM outputs.

## Diagnostics

The generated method validates policy/type combinations before native invocation.

An analyzer can provide earlier diagnostics for:

- `ComObject` with a projected WinRT `T`.
- `WindowsRuntime` without C#/WinRT.
- Unsupported non-interface `T`.
- `WindowsRuntime` with a generated COM `T` while dynamic interface casting is disabled.

If runtime type classification is not reliable enough for `Default`, the analyzer becomes required for a statically known WinRT `T`.

## Unique COM ownership

Unique wrapper ownership is separate from COM-versus-WinRT selection.

The existing prototype included `ComObjectUniqueInstance` and validated deterministic release through `UniqueComInterfaceMarshaller<T>`. The primary policy proposal does not require that enum value. It can be added in a later proposal or omitted without weakening the WinRT fix.

## Prototype evidence

The policy implementation prototype on draft PR #1771 validates:

- Type-directed `Default` for generated COM and projected WinRT interfaces.
- Explicit `WindowsRuntime` for `object`.
- Explicit `ComObject` for `object` and generated COM interfaces.
- Raw flat P/Invoke declarations.
- Same-IID raw COM companions.
- Native RCWs, managed COM proxies, and direct managed implementations.
- .NET 9, .NET 10, and Native AOT.
- Balanced cleanup on success and projection failure.

The prototype currently includes the optional unique-ownership value. The core COM-versus-WinRT design does not depend on it.

The separate adaptive prototype validates the projection building block required for explicit `WindowsRuntime` with a generated COM `T`: after querying the returned COM pointer for `IInspectable`, a C#/WinRT wrapper can dynamically cast to and invoke a source-generated COM interface. The policy prototype does not yet wire this combination end to end.

## Required validation

- `Default` with native COM, projected WinRT, generated COM, and `object`.
- Explicit `WindowsRuntime` with projected WinRT, generated COM, and `object`.
- Explicit `ComObject` with generated COM and `object`.
- Invalid combinations fail before native invocation.
- Flat P/Invoke and COM interface methods.
- Native server and managed `[GeneratedComClass]` server.
- Direct managed implementation and COM-proxied managed implementation.
- Managed implementation returning WinRT, inspectable COM, and non-inspectable COM objects.
- Dynamic interface casting enabled and disabled.
- Null outputs, failed HRESULTs, and projection failures.
- Multiple IID/output pairs.
- C#/WinRT absent.
- Native AOT publish and execution.
- Full generator and runtime suites.

## Open questions

1. Is preserving zero detection QIs on ordinary COM outputs worth the policy API and raw-companion complexity?
2. Is cached classification reliable enough for `Default`, or should known WinRT `T` require explicit `WindowsRuntime` through an analyzer?
3. Should explicit `WindowsRuntime` with a generated COM `T` be supported as proposed?
4. Is the same-IID raw companion acceptable as a generated implementation detail?
5. Should unique COM ownership remain a separate follow-up or be added as a fourth policy value?
