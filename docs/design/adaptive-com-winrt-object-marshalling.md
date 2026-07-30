# Adaptive COM and WinRT object out-parameter marshalling

## Status

Proposed alternative.

Related documents:

- [Caller-selected COM and WinRT object out-parameter marshalling](caller-selected-com-winrt-object-marshalling.md)
- [COM and WinRT object out-parameter marshalling options](com-winrt-object-marshalling-options.md)

This note explores accepting one `QueryInterface(IInspectable)` operation for each relevant COM object out parameter in exchange for substantially simpler generated APIs and automatic COM/WinRT projection in the generated caller path.

## Summary

CsWin32 currently projects an object returned through an `IID`/`void**` pair as a source-generated COM `ComObject`. That works for ordinary COM interfaces, but it prevents later casts to C#/WinRT interfaces such as `Windows.Storage.IStorageItem`.

The [caller-selected proposal](caller-selected-com-winrt-object-marshalling.md) avoids probing returned objects. It therefore needs a caller-visible policy, type classification, raw ABI paths, and same-IID companion interfaces for source-generated COM methods.

This alternative makes the opposite tradeoff:

1. Query every relevant returned object for `IInspectable`.
2. When the QI succeeds, create a C#/WinRT wrapper.
3. When it returns `E_NOINTERFACE`, create the existing source-generated COM wrapper.
4. Use the same bidirectional custom marshaller on flat P/Invokes and `[GeneratedComInterface]` methods.

The C#/WinRT wrapper can dynamically cast to source-generated COM interfaces on .NET 8 and later. An inspectable object therefore remains usable through both its WinRT and COM interfaces. A non-inspectable object remains a `ComObject`.

This removes the need for:

- A caller-visible COM-versus-WinRT marshalling policy.
- An analyzer that requires callers to identify WinRT output types.
- Raw companion P/Invokes.
- Same-IID raw `[GeneratedComInterface]` companions.
- Temporary CCW/raw-RCW adapters for direct managed implementations.

Unique COM wrapper ownership is not part of this proposal. It requires caller intent and can be designed independently.

## Problem

Methods following the `IID_PPV_ARGS` pattern return an ABI interface pointer:

```csharp
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);
```

The managed projection must decide which wrapper family owns that pointer.

Today, source-generated COM uses `ComInterfaceMarshaller<T>`. For a WinRT object this produces a `System.Runtime.InteropServices.Marshalling.ComObject`, which cannot be cast to the C#/WinRT projection:

```text
ComObject -> Windows.Storage.IStorageItem
```

The requested managed type is not always enough to express the intended projection. A caller may receive `object` or a COM interface and cast to a WinRT interface later. The initial wrapper must support that future use.

The same ambiguity exists on a COM interface implemented in managed code. Its `out object` may be assigned either:

```csharp
ppv = storageFile; // C#/WinRT object
```

or:

```csharp
ppv = comObject; // Source-generated COM object
```

The generated RCW and CCW path should support both without changing the managed interface method for each call.

## Goals

- Project inspectable outputs through C#/WinRT.
- Project non-inspectable outputs through source-generated COM.
- Allow an inspectable object to remain callable through source-generated COM interfaces it also implements.
- Allow a managed `[GeneratedComClass]` implementation to return either a C#/WinRT object or a COM object from the same `out object` parameter when consumed through the generated caller path.
- Apply the same rule to flat P/Invokes and COM interface methods.
- Preserve the existing generic friendly call shape.
- Support Native AOT.
- Release every native reference on success and failure.

## Non-goals

- Select unique versus identity-cached COM ownership.
- Avoid `QueryInterface(IInspectable)` on relevant object outputs.
- Apply adaptive projection to every interface parameter or return value.
- Change input parameter marshalling.
- Infer or enforce an adjacent `riid` in a managed COM server stub.
- Guarantee concrete WinRT runtime-class wrapper identity under Native AOT.
- Support built-in COM in the first implementation.

## Proposed design

### Adaptive object marshaller

CsWin32 generates one custom marshaller for object outputs associated with an IID:

```csharp
[CustomMarshaller(
    typeof(object),
    MarshalMode.ManagedToUnmanagedOut,
    typeof(ComOrWinRTObjectMarshaller))]
[CustomMarshaller(
    typeof(object),
    MarshalMode.UnmanagedToManagedOut,
    typeof(ComOrWinRTObjectMarshaller))]
internal static unsafe class ComOrWinRTObjectMarshaller
{
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    private static readonly Guid IID_IInspectable =
        new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");

    public static object? ConvertToManaged(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        Guid iid = IID_IInspectable;
        int hr = Marshal.QueryInterface(value, in iid, out nint inspectable);
        if (hr >= 0)
        {
            try
            {
                return WinRT.MarshalInspectable<object>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }

        if (hr != E_NOINTERFACE)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return ComInterfaceMarshaller<object>.ConvertToManaged((void*)value);
    }

    public static nint ConvertToUnmanaged(object? value) =>
        (nint)ComInterfaceMarshaller<object>.ConvertToUnmanaged(value);

    public static void Free(nint value) =>
        ComInterfaceMarshaller<object>.Free((void*)value);
}
```

The generated interop declaration uses the marshaller only for the object output associated with the IID:

```csharp
void BindToHandler(
    IBindCtx? pbc,
    Guid* bhid,
    Guid* riid,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller))] out object ppv);
```

The QI result, not the caller's generic type, selects the wrapper family.

Unexpected QI failures propagate. Only `E_NOINTERFACE` means that the value should use the COM fallback.

### Native-to-managed output

For an RCW or P/Invoke call:

1. Native code returns one owned interface reference.
2. The marshaller queries that identity for `IInspectable`.
3. On success, `MarshalInspectable<object>.FromAbi` creates or resolves the C#/WinRT wrapper.
4. On `E_NOINTERFACE`, `ComInterfaceMarshaller<object>.ConvertToManaged` creates or resolves a `ComObject`.
5. Generated cleanup releases the original output reference. The temporary `IInspectable` QI reference is released separately.

This also handles a managed COM server that returns a bare `IUnknown`. The QI is performed by the caller-side marshaller rather than relying on the server to return a particular interface pointer.

### Managed-to-native output

For a `[GeneratedComClass]` implementation:

```csharp
public void BindToHandler(..., out object ppv)
{
    ppv = value;
}
```

`ComInterfaceMarshaller<object>.ConvertToUnmanaged` already accepts:

- A C#/WinRT wrapper.
- A source-generated COM wrapper.
- A managed object exposed through source-generated COM.

The same managed interface method can therefore return either a WinRT object or a COM object. The caller-side adaptive marshaller chooses the corresponding managed projection when the value crosses back through an RCW.

### Friendly generic method

The existing friendly method can remain conceptually simple:

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv)
    where T : class
{
    Guid iid = GetIID<T>();
    @this.BindToHandler(pbc, &bhid, &iid, out object value);
    ppv = (T)value;
}
```

IID selection remains type-directed:

- C#/WinRT types use `WinRT.GuidGenerator.CreateIID(typeof(T))`.
- Source-generated COM interfaces use `typeof(T).GUID`.
- `object` uses `IID_IUnknown`.

This classification selects the requested native interface. It no longer selects the managed wrapper family.

The cast works for all supported cases:

- A projected WinRT object implements its projected WinRT interfaces.
- A C#/WinRT `IInspectable` wrapper dynamically supports source-generated COM interfaces whose IID the object implements.
- A `ComObject` dynamically supports source-generated COM interfaces whose IID the object implements.

`object` receives the natural adaptive result: a C#/WinRT wrapper for inspectable values and a `ComObject` for other values.

Some APIs do not accept `IID_IUnknown` as a meaningful request. Callers of those APIs must use the semantic interface type instead of `object`. Adaptive projection does not change that native contract.

## COM interface impact

This option directly simplifies source-generated COM interfaces.

### One interface declaration

The public `[GeneratedComInterface]` declaration carries the adaptive marshaller on `out object`. The source-generated COM infrastructure selects:

- `ManagedToUnmanagedOut` for RCW calls, which runs `ConvertToManaged`.
- `UnmanagedToManagedOut` for CCW calls, which runs `ConvertToUnmanaged`.

No second interface with the same IID is required.

### Managed implementations

The implementation method remains safe and natural:

```csharp
public void BindToHandler(..., out object ppv)
{
    ppv = this.returnWinRT
        ? this.storageFile
        : this.comObject;
}
```

A direct call on the managed implementation returns the assigned object normally. A call through a COM proxy uses the adaptive marshaller and produces the corresponding C#/WinRT or COM projection.

There is no raw pointer in the managed implementation signature and no temporary adapter when the friendly method is called directly on the managed object.

### Inspectable objects used as COM

An object may implement `IInspectable` and unrelated COM interfaces. The adaptive rule chooses a C#/WinRT wrapper for that identity, but this does not prevent COM use.

On .NET 8 and later, C#/WinRT's `IWinRTObject` dynamic interface casting checks source-generated COM interface metadata through `StrategyBasedComWrappers`, queries the generated IID, and supplies the generated vtable. A normal cast to the CsWin32 interface therefore works:

```csharp
IStream stream = (IStream)inspectableWrapper;
stream.Read(...);
```

The prototype validated this with a shell stream whose primary adaptive wrapper was `WinRT.IInspectable`.

The first implementation should require C#/WinRT's dynamic interface cast support to remain enabled. If that feature is disabled, inspectable objects cannot be relied upon to expose arbitrary source-generated COM interfaces through the WinRT wrapper.

This is a hard runtime requirement, not an optimization. The generated friendly method should detect a request for a source-generated COM `T` while dynamic interface casting is disabled and throw a targeted `NotSupportedException` that names the `CsWinRTEnableIDynamicInterfaceCastableSupport` setting. Otherwise the failure appears later as an unhelpful cast exception.

## Flat P/Invoke impact

The same `[MarshalUsing]` attribute can be applied to a flat method's object output. The existing public interop declaration can perform adaptive projection directly.

No raw duplicate `[LibraryImport]` entry point is required, so this option does not introduce the policy proposal's extra raw P/Invoke surface.

The first implementation remains scoped to the source-generated interop modes in which the custom marshaller is available.

## Multiple IID/output pairs

Each recognized IID/output pair independently uses the same object marshaller. The one SDK method with two canonical pairs remains one COM declaration:

```csharp
void GetCurrentResourceAndCommandQueue(
    in Guid riidResource,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller))] out object resource,
    in Guid riidQueue,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller))] out object queue);
```

The friendly generic overload computes and passes each requested IID independently. Each output is then adaptively projected and released independently. The marshaller does not need to read either sibling IID because those parameters select what native code returns, while each output pointer independently determines its managed wrapper family.

Unlike the policy proposal, this does not add one caller-visible policy parameter per output. Generator integration still must validate the two-output method and the 15 canonical pairs that are not the final two parameters.

## Adjacent IID behavior in managed COM servers

The marshaller operates on one parameter and cannot read the neighboring `riid`.

When a managed implementation assigns `out object`, `ComInterfaceMarshaller<object>` may return the object's `IUnknown` rather than the exact interface requested by `riid`. The generated adaptive caller tolerates this because it performs the QI needed for its managed projection and later casts.

This is sufficient for calls that use the generated CsWin32 RCW/friendly path. It is not safe to claim general managed COM server support for arbitrary native clients: a native caller may immediately dereference `ppv` as the interface requested by `riid`, while the generated CCW may have returned its identity pointer.

Managed implementations of IID/`void**` methods should therefore be considered supported by this proposal only when their callers use the generated adaptive projection, or when the implementation independently guarantees that the returned pointer matches `riid`.

If managed COM servers must satisfy external native callers that immediately dereference `ppv` as the requested interface, that needs a separate design. Possible approaches include:

- A managed result carrier containing both the object and requested IID.
- Generator support for a marshaller that can consume a sibling parameter.
- A lower-level implementer signature with an explicit raw output pointer.

That issue exists with the current object-shaped generated COM method and is not introduced by adaptive detection. This proposal does, however, route more generated calls through the object-shaped method instead of a raw pointer path, so it increases the importance of resolving or clearly scoping the limitation.

## Cost and behavior changes

### Additional QI

Every recognized object output performs `QueryInterface(IInspectable)`.

This is the central tradeoff. It replaces caller policy and generated raw-companion complexity with a predictable per-output runtime cost.

The QI is limited to object outputs associated with an IID/`void**` pattern. It is not added to every COM call or interface parameter.

It also introduces a new failure point. A nonconforming or disconnected COM object that returns an HRESULT other than `E_NOINTERFACE` for the probe now fails the call, whereas the existing COM-only path never issued that QI.

### Wrapper selection

An inspectable object is represented primarily by its C#/WinRT wrapper, even when the immediate caller asks for a COM interface. Source-generated COM access remains available through dynamic interface casting.

This changes managed wrapper identity and type observations for inspectable objects that previously appeared as `ComObject`.

### Existing generated callers

The adaptive rule is an observable behavior change for existing generated calls. A call that previously returned `ComObject` can return a C#/WinRT wrapper when the native identity implements `IInspectable`, even when its source signature is unchanged.

CsWin32 projections are primarily internal, so this proposal does not require forwarding overloads or cross-module ABI compatibility. Consumers must still accept the wrapper-family transition and avoid depending on concrete wrapper types. If preserving the old behavior for existing generated code is required, adaptive projection would need an adoption switch or version boundary.

### C#/WinRT dependency

When C#/WinRT is referenced, CsWin32 emits the adaptive marshaller. Without C#/WinRT, the output retains the existing COM-only marshalling behavior.

### Native AOT

The design uses generated COM metadata and C#/WinRT's .NET 8+ dynamic vtable path. It does not require runtime-generated code or reflection-based interface stubs.

Native AOT preserves interface usability, but it may not preserve the same concrete managed wrapper type as JIT. In the prototype, JIT projected the storage item as `Windows.Storage.StorageFile`, while Native AOT produced a generic `WinRT.IInspectable` wrapper that still cast to and implemented `IStorageItem`. Callers must rely on the requested interface contract rather than a concrete runtime-class wrapper type.

## Unique COM ownership

Unique wrapper ownership is intentionally separate.

Adaptive COM-versus-WinRT selection can be determined from the returned native object. Unique versus identity-cached COM ownership cannot; it is a caller policy.

A future proposal can add a distinct API for callers that need deterministic independent release, without making the core WinRT correctness fix depend on that API. If no satisfactory shape is found, unique ownership can be omitted.

## Prototype results

An isolated prototype applied one bidirectional custom marshaller to a generated `IShellItem.BindToHandler` method.

It passed on .NET 9, .NET 10, and Native AOT for:

- A native WinRT output projected as `Windows.Storage.IStorageItem`.
- A native inspectable shell stream projected as `WinRT.IInspectable`, cast to a source-generated `IStream`, and invoked successfully.
- A native non-inspectable `IBindCtx` projected as `ComObject`.
- A managed `[GeneratedComClass]` returning a WinRT object through `out object`.
- A managed `[GeneratedComClass]` returning an inspectable COM object through the same `out object`.
- A managed `[GeneratedComClass]` returning a non-inspectable COM object through the same `out object`.

The prototype required no raw companion interface and no managed receiver adapter.

Under Native AOT, the WinRT output used a generic `WinRT.IInspectable` wrapper instead of the concrete `StorageFile` wrapper seen under JIT. The requested `IStorageItem` interface remained fully usable.

## Decision comparison

See [COM and WinRT object out-parameter marshalling options](com-winrt-object-marshalling-options.md) for the side-by-side evaluation of this proposal and the caller-selected alternative.

## Required validation

- Native WinRT, inspectable COM, and non-inspectable COM outputs.
- Projected WinRT interfaces, mapped interfaces, generated COM interfaces, and `object`.
- Actual method invocation after casting an inspectable wrapper to a generated COM interface.
- Flat P/Invoke and `[GeneratedComInterface]` methods.
- Native COM server and managed `[GeneratedComClass]` server.
- Managed server returning WinRT, inspectable COM, and non-inspectable COM values.
- Null outputs and failed HRESULTs.
- Unexpected QI failures propagate instead of falling back.
- Multiple IID/output pairs.
- C#/WinRT absent.
- C#/WinRT dynamic interface cast support disabled.
- Clear diagnostic when a generated COM cast requires disabled C#/WinRT dynamic interface support.
- Native AOT publish and execution.
- Native AOT concrete wrapper type versus interface usability.
- Full generator and runtime suites.

## Open questions

1. Is one `QueryInterface(IInspectable)` per relevant object output an acceptable cost for the simpler and more flexible model?
2. Should adaptive marshalling apply only to canonical IID/`void**` pairs, or to other object-shaped COM outputs?
3. Is requiring C#/WinRT dynamic interface cast support acceptable for inspectable objects used through generated COM interfaces?
4. Does the managed COM server scenario need to support arbitrary external native callers that require `ppv` to already match `riid`?
5. Should unique COM ownership be omitted entirely or pursued as an independent follow-up?
