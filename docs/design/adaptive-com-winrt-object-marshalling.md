# Automatic COM and Windows Runtime object out-parameter marshalling

## Status

Accepted.

CsWin32 will automatically detect Windows Runtime objects returned through recognized COM `IID`/`void**` out-parameter pairs. The automatic behavior is enabled by default and can be disabled globally in `NativeMethods.json`.

The caller-selected policy described in [Caller-selected COM and WinRT object out-parameter marshalling](caller-selected-com-winrt-object-marshalling.md) was considered but not selected. Unique COM wrapper ownership remains separate work.

## Motivation

CsWin32 projects COM object outputs as COM wrappers. That is correct for ordinary COM, but it prevents an object returned through an `IID`/`void**` pair from being used as a C#/WinRT projection:

```csharp
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);
```

The native object returned by `BindToHandler` implements `IInspectable`, but COM-only marshalling creates a `ComObject`. That wrapper cannot safely provide the C#/WinRT `IStorageItem` behavior. The problem also occurs when the immediate output type is `object` or a COM interface and the caller casts to a WinRT interface later.

Callers should not have to know which wrapper family to request. The returned native identity already provides the authoritative answer:

- An identity that implements `IInspectable` should be projected through C#/WinRT.
- An identity that returns `E_NOINTERFACE` for `IInspectable` should use normal COM projection.

The extra `QueryInterface(IInspectable)` is accepted in exchange for automatic behavior and substantially simpler generated APIs.

## Decision

For each eligible COM object output:

1. Request the native interface identified by the friendly method's `T`.
2. Query the returned identity for `IInspectable`.
3. On success, project the value with `WinRT.MarshalInspectable<object>.FromAbi`.
4. On `E_NOINTERFACE`, use the normal COM projection.
5. Propagate every other QI failure.

This rule applies to:

- Source-generated flat P/Invokes.
- `[GeneratedComInterface]` RCW calls.
- `[GeneratedComInterface]` CCW calls to managed implementations.
- Built-in P/Invoke and `[ComImport]` friendly overloads.

The generated friendly signature remains:

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv)
    where T : class;
```

No caller-visible marshalling enum, raw companion method, same-IID companion interface, or analyzer is required.

## Configuration

Automatic projection is enabled by default:

```json
{
  "comInterop": {
    "autoWinRTMarshalling": true
  }
}
```

It can be disabled for a generated projection:

```json
{
  "comInterop": {
    "autoWinRTMarshalling": false
  }
}
```

Disabling the option preserves the existing COM-only behavior and avoids the additional `QI(IInspectable)`.

The option has no effect when:

- `allowMarshaling` is `false`.
- C#/WinRT is not referenced.
- The target framework does not provide the required custom-marshalling support for source-generated interop.

In those cases CsWin32 emits the existing projection without C#/WinRT dependencies.

## Eligible methods

The initial implementation recognizes the canonical final parameter pair:

```text
Guid* riid, [ComOutPtr] void** ppv
```

The pair must be the final two metadata parameters. The existing generic-friendly-overload option remains independent: disabling `friendlyOverloads.comOutPtrGenericOverloads` suppresses the generic overload but does not disable source-generated ABI marshalling.

A metadata scan found:

- 420 generator-relevant methods with one canonical pair.
- 15 of those methods with a non-final pair, which remain future work.
- One method with two canonical pairs, which remains future work.

## IID selection

The requested native IID remains type-directed:

- `object` uses `IID_IUnknown`.
- A C#/WinRT type uses `WinRT.GuidGenerator.CreateIID(typeof(T))`.
- A generated COM type uses `typeof(T).GUID`.

The generic type parameter carries:

```csharp
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
```

This preserves the fields used by C#/WinRT IID generation under trimming and Native AOT.

IID selection determines which native interface is requested. It does not select the managed wrapper family; the returned identity does that through the `IInspectable` probe.

## Adaptive output marshaller

Source-generated interop uses one generated object marshaller:

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
    public static object ConvertToManaged(nint value);
    public static nint ConvertToUnmanaged(object value);
    public static void Free(nint value);
}
```

`ConvertToManaged`:

- Returns `null` for a null native pointer.
- Queries `IInspectable`.
- Projects through C#/WinRT on success.
- Falls back to `ComInterfaceMarshaller<object>` only for `E_NOINTERFACE`.

The original output reference and the temporary `IInspectable` QI reference are released independently.

## Flat P/Invoke

Eligible `[LibraryImport]` outputs replace `[MarshalAs(UnmanagedType.Interface)]` with:

```csharp
[MarshalUsing(typeof(ComOrWinRTObjectMarshaller))]
out object ppv
```

The friendly overload computes the IID, invokes the existing declaration, and casts the adaptively projected object to `T`.

No duplicate raw P/Invoke is generated.

## Source-generated COM interfaces

Generated COM interfaces apply the adaptive object marshaller in both directions:

- RCW: native code returns an interface pointer to managed code.
- CCW: a managed implementation returns an object to a native caller.

For CCWs, `ComInterfaceMarshaller<object>.ConvertToUnmanaged` returns the object's identity pointer.
A generated managed consumer passes that pointer through the adaptive input projection, then the
friendly overload casts the projected object to `T`. The cast performs the required interface QI.

### Generated managed signature

An eligible generated COM method has this managed shape:

```csharp
void BindToHandler(
    IBindCtx? pbc,
    Guid* bhid,
    Guid* riid,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller))]
    out object ppv);
```

### Managed-to-native output

For a managed implementation:

```csharp
public unsafe void BindToHandler(..., Guid* riid, out object ppv)
{
    ppv = value;
}
```

the output marshaller converts `value` to its COM identity. The consuming adaptive projection and
generic cast select the requested interface.

This permits the same managed method to return:

- A managed or projected WinRT object.
- An inspectable COM object.
- A non-inspectable COM object.
- `null`.

Producing the exact interface pointer named by `riid` for arbitrary native callers of managed
implementations is a separate generated COM marshalling concern and is not added by this proposal.

## Built-in COM interop

Classic `[ComImport]` and `DllImport` do not honor source-generated `[MarshalUsing]` marshallers. CsWin32 therefore adapts the object in the friendly overload after built-in COM marshalling:

1. Receive the built-in COM wrapper as `object`.
2. Call `Marshal.GetIUnknownForObject`.
3. Query the identity for `IInspectable`.
4. Project through C#/WinRT on success.
5. Return the original built-in COM wrapper on `E_NOINTERFACE`.
6. Release the temporary identity and QI references.

This creates a transient built-in RCW before an inspectable value is reprojected. CsWin32 must not call `FinalReleaseComObject` on it because the RCW may be identity-cached and shared.

Runtime validation must invoke a WinRT member after adaptation. A cast alone is insufficient because a classic COM wrapper can appear castable to a WinRT interface while dispatching through the wrong vtable.

## Inspectable objects used through COM interfaces

An inspectable object is represented by its C#/WinRT wrapper even when the immediate `T` is a generated COM interface.

On .NET 8 and later, C#/WinRT dynamic interface casting can query source-generated COM IIDs and use their generated vtables. An inspectable shell stream can therefore be projected as `WinRT.IInspectable`, cast to CsWin32's generated `IStream`, and invoked successfully.

Consumers that disable C#/WinRT dynamic interface casting cannot rely on this behavior.

## Native AOT

The implementation uses source-generated COM metadata, custom marshallers, and C#/WinRT's generated projection support. It does not require runtime-generated interop stubs.

Native AOT callers must rely on the requested interface contract rather than a concrete runtime-class wrapper. JIT may return `Windows.Storage.StorageFile` where Native AOT returns a generic `WinRT.IInspectable` wrapper that still implements `IStorageItem`.

The integration suite publishes a Native AOT package-consumption application.

## Behavior and compatibility

This is an observable wrapper-family change:

- Inspectable values that previously appeared as COM wrappers now appear as C#/WinRT wrappers.
- Non-inspectable values remain COM wrappers.
- `object` receives the natural adaptive result.

CsWin32 projections are primarily generated as internal implementation details. Preserving the previous friendly or ABI signature across generated assemblies is not a design constraint.

The generated native ABI remains unchanged.

## Cost and failure behavior

Every eligible output performs one `QI(IInspectable)`.

Only `E_NOINTERFACE` selects COM fallback. Other HRESULT failures propagate because they can represent disconnection, proxy failure, or a broken COM implementation.

The probe is limited to recognized object outputs; it is not added to every COM parameter or return value.

## Non-goals

- Unique or independently releasable COM wrapper ownership.
- Input parameter marshalling changes.
- Applying adaptive projection to every fixed-type COM output.
- Non-final or multiple IID/output pairs in the initial implementation.
- Preserving concrete WinRT runtime-class wrapper identity.
- Exact-`riid` output pointers from managed implementations consumed directly by arbitrary native
  callers.

## Validation

The implementation includes:

- Generator-shape tests for flat P/Invoke, generated COM, built-in COM, C#/WinRT absence, and both opt-outs.
- Runtime tests for native WinRT, inspectable COM, non-inspectable COM, `object`, and null outputs.
- Managed `[GeneratedComClass]` tests returning WinRT, inspectable COM, and non-inspectable COM values.
- Built-in COM runtime tests that invoke `IStorageItem.Name`.
- Enabled and disabled runtime tests demonstrating the behavior change.
- Native AOT package-consumption publish.
