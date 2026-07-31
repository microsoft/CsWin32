# Automatic COM and Windows Runtime object out-parameter marshalling

## Status

Accepted.

CsWin32 will automatically detect Windows Runtime objects returned through recognized COM `IID`/`void**` out-parameter pairs. The automatic behavior is enabled by default and can be disabled globally in `NativeMethods.json`.

The caller-selected policy was considered but not selected. Unique COM wrapper ownership remains separate work.

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

This rule applies to source-generated flat P/Invokes, generated COM RCW and CCW calls, and built-in COM friendly overloads.

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

Set the option to `false` to preserve COM-only projection and avoid the additional `QI(IInspectable)`.

The option has no effect when marshaling is disabled, C#/WinRT is absent, or the target framework lacks required custom-marshalling support.

## Eligible methods

The initial implementation recognizes a final metadata pair:

```text
Guid* riid, [ComOutPtr] void** ppv
```

Non-final pairs and the one SDK method with two pairs remain future work. Disabling generic COM out-pointer friendly overloads does not disable source-generated ABI marshalling.

## IID selection

- `object` uses `IID_IUnknown`.
- C#/WinRT types use `WinRT.GuidGenerator.CreateIID(typeof(T))`.
- Generated COM types use `typeof(T).GUID`.

The generic `T` is annotated with `DynamicallyAccessedMembers(PublicFields)` for trimming and Native AOT.

IID selection chooses the native interface. The returned identity chooses the managed wrapper family.

## Adaptive output marshaller

Source-generated interop uses one object marshaller for `ManagedToUnmanagedOut` and `UnmanagedToManagedOut`.

Native-to-managed conversion queries `IInspectable`, uses C#/WinRT on success, falls back to `ComInterfaceMarshaller<object>` only for `E_NOINTERFACE`, and releases the original and temporary QI references independently.

Eligible `[LibraryImport]` declarations apply `[MarshalUsing]` directly to `out object`; no duplicate raw P/Invoke is generated.

## Generated COM interfaces

The object marshaller alone cannot make a managed CCW ABI-correct. `ComInterfaceMarshaller<object>` normally returns an identity pointer, while native callers require the exact interface named by the sibling `riid`.

CsWin32 changes the generated managed shape without changing the native ABI:

```csharp
void BindToHandler(
    IBindCtx? pbc,
    Guid* bhid,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller.IidMarshaller))]
    in Guid riid,
    [MarshalUsing(typeof(ComOrWinRTObjectMarshaller))]
    out object ppv);
```

The IID marshaller passes the IID through on RCW calls. On CCW calls it pushes the IID onto a thread-local stack for the duration of managed dispatch. A stack supports nested and reentrant calls and is popped during generated cleanup.

For managed-to-native output, the object marshaller converts the returned object to its COM identity, reads the active IID, queries for that IID, returns the requested interface pointer, and releases the temporary identity reference.

Managed implementations may return WinRT objects, inspectable COM objects, non-inspectable COM objects, or `null`. Raw native test clients verify that the returned pointer is the exact requested interface, not merely `IUnknown`.

## Built-in COM interop

Classic `[ComImport]` and `DllImport` do not honor source-generated custom marshallers. Their friendly overloads post-process the built-in wrapper:

1. Obtain its identity with `Marshal.GetIUnknownForObject`.
2. Query `IInspectable`.
3. Project through C#/WinRT on success.
4. Return the original built-in wrapper on `E_NOINTERFACE`.
5. Release temporary references.

This creates a transient built-in RCW. CsWin32 does not final-release it because it may be identity-cached and shared.

Runtime coverage invokes a WinRT member after adaptation; a cast alone is not sufficient to prove correct vtable dispatch.

## Inspectable objects used through COM interfaces

C#/WinRT wrappers on .NET 8 and later can dynamically expose source-generated COM interfaces. An inspectable shell stream can therefore be projected as `WinRT.IInspectable`, cast to CsWin32's `IStream`, and invoked.

Consumers that disable C#/WinRT dynamic interface casting cannot rely on this behavior.

## Native AOT

The design uses generated COM metadata and custom marshallers. Native AOT callers rely on interface contracts rather than concrete runtime-class wrapper types.

The integration suite publishes and executes a Native AOT app covering native WinRT and COM outputs, managed CCW outputs, and exact requested-IID pointer conformance.

## Behavior and cost

Inspectable values that previously appeared as COM wrappers now appear as C#/WinRT wrappers. Non-inspectable values remain COM wrappers.

Each eligible output adds one `QI(IInspectable)`. Only `E_NOINTERFACE` selects COM fallback; other failures propagate.

CsWin32 projections are primarily internal, so preserving previous generated source or managed ABI signatures is not a requirement. The native ABI remains unchanged.

## Non-goals

- Unique COM wrapper ownership.
- Input marshalling changes.
- Every fixed-type COM output.
- Non-final or multiple IID/output pairs in the initial implementation.
- Concrete WinRT runtime-class wrapper identity.

## Validation

Coverage includes generator-shape tests, source-generated and built-in runtime tests, enabled and disabled behavior, WinRT and COM outputs, managed servers, null output, raw native pointer conformance, and Native AOT execution.
