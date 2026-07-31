# COM and WinRT object out-parameter marshalling decision

## Status

Decided.

CsWin32 will use [automatic COM and Windows Runtime object out-parameter marshalling](adaptive-com-winrt-object-marshalling.md).

The [caller-selected policy](caller-selected-com-winrt-object-marshalling.md) remains documented as the principal alternative that was evaluated. Unique COM ownership remains separate work.

## Problem

Objects returned through COM `IID`/`void**` pairs may be ordinary COM objects or Windows Runtime objects. COM-only projection creates a `ComObject`, which cannot safely provide C#/WinRT behavior such as `IStorageItem.Name`.

The immediate generic type is not a complete signal:

- A caller may request `object` and cast to a WinRT interface later.
- A WinRT object may be requested through a COM interface and used through both families.
- A managed COM server may return either a COM or WinRT object from the same method.

The native identity is the authoritative source: successful `QI(IInspectable)` identifies an inspectable object, while `E_NOINTERFACE` identifies ordinary COM.

## Options evaluated

### Caller-selected policy

The caller-selected proposal adds:

```csharp
public enum ComOutPtrMarshalling
{
    Default,
    ComObject,
    WindowsRuntime,
}
```

The policy is passed to each generated friendly overload. `Default` classifies the closed generic `T`; callers must select `WindowsRuntime` explicitly when the immediate type does not reveal future WinRT use.

Flat P/Invokes need raw pointer declarations. Generated COM interfaces need same-IID raw companions and an adapter for direct managed implementations because the friendly call policy is not part of the COM method.

### Automatic runtime detection

The automatic proposal keeps the existing friendly API. Each eligible output:

1. Queries `IInspectable`.
2. Projects through C#/WinRT on success.
3. Falls back to COM only for `E_NOINTERFACE`.
4. Propagates other failures.

Source-generated declarations use a custom marshaller. Built-in COM friendly overloads post-process the built-in wrapper because `[ComImport]` does not support source-generated custom marshalling.

The behavior is enabled by default and can be disabled with:

```json
{
  "comInterop": {
    "autoWinRTMarshalling": false
  }
}
```

## Comparison

| Concern | Caller-selected policy | Automatic runtime detection |
| --- | --- | --- |
| Selection rule | Caller and closed `T` | Returned native identity |
| Ordinary COM output | No detection QI | One failed `QI(IInspectable)` |
| WinRT `T` | Usually inferred | Works automatically |
| `object` followed by WinRT cast | Requires explicit policy | Works automatically |
| COM `T` followed by WinRT cast | Requires explicit policy | Works when the object is inspectable |
| Non-inspectable COM | COM projection | Failed QI then COM projection |
| Public generated API | Adds policy enum and parameter | No new caller-facing API |
| Flat P/Invoke | Raw pointer path | Existing declaration with custom marshaller |
| Generated COM RCW | Same-IID raw companion | Custom output marshaller |
| Generated COM CCW | Raw companion and managed adapter | Coordinated IID and output marshallers |
| Managed implementation | Adapter path | Returns COM, WinRT, or null naturally |
| Multiple outputs | One policy per output | Same detection rule per output |
| Wrapper identity | Explicit policy | Determined by native identity |
| Runtime cost | Avoids detection QI | One QI per eligible output |
| Native AOT | Supported | Supported |
| Unique COM ownership | Separate | Separate |

## Decision

Automatic runtime detection is selected.

### Reasons

- Correctness should not depend on the immediate generic type revealing every later cast.
- `object` and COM-interface requests should remain usable as WinRT when the returned identity is inspectable.
- Callers expect COM APIs returning WinRT objects to work without a marshalling policy.
- The generated API remains small and does not multiply policy parameters across output pairs.
- One marshalling model covers flat P/Invoke, generated COM callers, managed COM implementations, and Native AOT.
- The extra `QI(IInspectable)` is acceptable for the recognized set of object outputs.

### Accepted consequences

- Inspectable values that previously appeared as COM wrappers now appear as C#/WinRT wrappers.
- Ordinary COM outputs pay one failed `QI(IInspectable)`.
- Unexpected QI failures other than `E_NOINTERFACE` now propagate.
- Inspectable values consumed through generated COM interfaces depend on C#/WinRT dynamic interface casting.
- JIT and Native AOT may expose different concrete wrappers while preserving the requested interface.

The opt-out exists for consumers that require prior COM-only wrapper behavior or cannot accept the probe.

## Managed COM server result

The initial prototypes identified an ABI problem: an object output marshaller alone returns an identity pointer, while a native caller requires the exact interface pointer named by the sibling `riid`.

The selected implementation solves this with coordinated marshallers:

- The generated managed `riid` parameter uses an IID input marshaller.
- During CCW dispatch, that marshaller places the requested IID in a thread-local stack.
- The object output marshaller queries the returned object's identity for that exact IID.
- Cleanup removes the IID context, including nested and reentrant calls.

The native ABI remains `Guid*` plus `void**`. Managed implementations can return WinRT objects, inspectable COM objects, non-inspectable COM objects, or `null`, and external native callers receive the exact requested interface pointer.

## Built-in COM result

Built-in `[ComImport]` does not honor `[MarshalUsing]`. The friendly overload therefore:

1. Obtains the built-in wrapper's identity.
2. Queries `IInspectable`.
3. Returns a C#/WinRT projection on success.
4. Returns the original wrapper on `E_NOINTERFACE`.

The transient built-in RCW is not final-released because it may be identity-cached and shared.

## Scope

The initial implementation recognizes a canonical final `Guid*`/`void**` pair.

Not included:

- Non-final pairs.
- The SDK method with two pairs.
- Fixed-type COM outputs that do not use the recognized pair.
- Input marshalling changes.
- Unique COM wrapper ownership.

## Validation

The selected design is covered by:

- Generator-shape tests.
- Source-generated runtime tests on .NET 9 and .NET 10.
- Built-in COM runtime tests that invoke WinRT members.
- An opt-out regression that preserves the former cast failure.
- Managed COM server tests for WinRT, inspectable COM, non-inspectable COM, and null.
- Raw native-style tests that verify exact requested-IID pointers.
- Native AOT publish and execution.
- Full Windows build, ordinary test, and hardware-dependent test suites.
