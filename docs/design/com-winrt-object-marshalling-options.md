# COM and WinRT object out-parameter marshalling options

## Status

Decision document.

This note compares two formal proposals for projecting objects returned through COM `IID`/`void**` out-parameter pairs:

1. [Caller-selected COM and WinRT object out-parameter marshalling](caller-selected-com-winrt-object-marshalling.md)
2. [Adaptive COM and WinRT object out-parameter marshalling](adaptive-com-winrt-object-marshalling.md)

The primary decision is whether CsWin32 should preserve ordinary COM projection unless the caller selects WinRT, or query each relevant returned object for `IInspectable` and choose the wrapper family from the runtime identity.

Unique COM ownership is intentionally outside this decision.

## Shared problem

The current source-generated COM path creates a `ComObject` for IID/output values. That wrapper cannot later become a C#/WinRT projection such as `IStorageItem`.

The fix must support:

- Native methods returning COM or WinRT objects.
- Generated COM interface RCW calls.
- Managed `[GeneratedComClass]` implementations returning COM or WinRT objects.
- `object`, projected WinRT interfaces, and generated COM interfaces.
- Native AOT.

## Shared prototype findings

Both proposals build on facts validated by the policy and adaptive prototypes:

- `ComInterfaceMarshaller<object>` accepts C#/WinRT wrappers, source-generated COM wrappers, and managed generated-COM objects on CCW output.
- After projection from an `IInspectable` pointer, C#/WinRT wrappers on .NET 8 and later can dynamically cast to source-generated COM interfaces by using generated IID and vtable metadata.
- A managed object-shaped CCW output cannot automatically correlate its pointer with a sibling `riid`.
- Native AOT may use a generic `WinRT.IInspectable` wrapper while preserving the requested interface.
- `out object` is not valid for APIs that require a semantic IID rather than `IID_IUnknown` or `IID_IInspectable`.

## Side-by-side comparison

| Concern | Caller-selected policy | Adaptive `IInspectable` QI |
| --- | --- | --- |
| Selection rule | Caller and closed `T` | Returned native identity |
| Ordinary COM output | Existing COM projection; no detection QI | One failed `QI(IInspectable)` |
| WinRT output with WinRT `T` | `Default` selects WinRT | QI selects WinRT |
| `out object` followed by WinRT cast | Caller must select `WindowsRuntime` | Works automatically |
| COM `T` followed by WinRT cast | Caller must select `WindowsRuntime` | Works automatically if inspectable |
| Inspectable object used through COM | Caller chooses COM or WinRT wrapper | WinRT wrapper dynamically casts to COM |
| Non-inspectable COM object | `ComObject` | Failed QI then `ComObject` |
| Wrapper identity | Explicit and predictable from policy | May change from `ComObject` to C#/WinRT based on identity |
| Existing generated calls | `Default` preserves COM projection for `object` and generated COM `T` | Inspectable outputs can change wrapper family without a source-signature change |
| Public API | Adds `ComOutPtrMarshalling` | No policy API |
| Type classification | Required for `Default` | Required only for requested IID |
| Analyzer | Possible fallback for WinRT `T` | Not needed for wrapper selection |
| Flat P/Invoke | Raw `out nint` declaration | Existing object declaration plus custom marshaller |
| COM interface RCW | Same-IID raw companion | Existing interface plus custom marshaller |
| COM interface CCW used by generated caller | Raw companion plus direct-managed adapter | One bidirectional custom marshaller |
| Direct managed implementation | Temporary CCW/raw-RCW adapter | Normal managed call |
| Multiple IID/output pairs | One policy per output | Same adaptive rule per output |
| C#/WinRT dynamic cast dependency | Only when `WindowsRuntime` is selected for generated COM `T` | Whenever an inspectable result is consumed as generated COM `T` |
| Native AOT | Explicit projection; interface contract required | Adaptive projection; interface contract required |
| Unexpected `IInspectable` QI failure | No detection QI | New failure unless result is `E_NOINTERFACE` |
| Generated complexity | Policy helpers, raw paths, companions, adapter | Custom marshaller and feature-switch guard |
| Runtime cost | No detection QI; raw-companion dispatch and direct-managed adaptation still have costs | One QI per relevant output |
| Unique COM ownership | Separate future work | Separate future work |

## Caller-selected policy evaluation

### Strengths

- Preserves current COM behavior and cost for the common case.
- Makes wrapper identity an explicit caller decision.
- Avoids changing an inspectable COM object into a WinRT wrapper unless requested.
- Supports callers that know the semantic intent even when `T` is `object`.
- Can provide a direct path to future ownership policies without affecting adaptive detection.

### Costs

- Adds public generated API surface.
- Ambiguous calls can be wrong if the caller omits the explicit policy.
- A later WinRT cast from `object` cannot be inferred.
- Source-generated COM methods need raw same-IID companions.
- Direct managed implementations need a temporary CCW/raw-RCW adapter.
- Raw-companion dispatch and direct-managed adaptation can add interop work even though ordinary COM outputs avoid the detection QI.
- Multiple outputs add one policy parameter per output.
- `Default` depends on reliable C#/WinRT type classification or an analyzer.
- Explicit `WindowsRuntime` with a generated COM `T` adds a caller-requested `QI(IInspectable)` after the native call returns the requested COM interface.

### COM interface consequence

The policy belongs to the friendly caller, not the native COM method. The public `[GeneratedComInterface]` declaration cannot carry it.

The design therefore keeps the managed `out object` interface for implementers but invokes the native slot through a raw same-IID companion. This preserves caller control, at the cost of the most generated machinery.

## Adaptive QI evaluation

### Strengths

- Fixes WinRT projection without a new caller-facing API.
- Handles `object` and future WinRT casts automatically.
- Uses one declaration and one bidirectional marshaller for COM RCW and CCW paths.
- Lets the same managed implementation return COM or WinRT values naturally.
- Avoids raw same-IID companions and direct-managed adapters.
- Multiple outputs do not grow the friendly API.

### Costs

- Adds `QI(IInspectable)` to every recognized object output.
- Changes wrapper identity for inspectable COM objects.
- Adds a failure point for QI results other than `E_NOINTERFACE`.
- Depends more broadly on C#/WinRT dynamic interface casting when an inspectable object is used through generated COM.
- AOT and JIT may expose different concrete wrapper types.
- Changes the observable wrapper family of existing calls that return inspectable objects.

### COM interface consequence

The custom marshaller can be placed directly on `out object` for both call directions:

- RCW output queries and projects the native value.
- CCW output converts either a WinRT or COM managed object to ABI.

This is the simplest COM-interface design, provided the QI cost and wrapper behavior are acceptable.

## Shared managed-server limitation

Neither proposal makes an object-shaped managed COM server automatically return a pointer already adjusted to the sibling `riid`.

Generated callers tolerate an identity pointer because they perform later QI during projection or casting. Arbitrary native callers may immediately dereference `ppv` as `riid` and are not covered unless the implementation guarantees the correct pointer.

Possible future solutions are shared:

- A result carrier containing the object and requested IID.
- Marshaller support for consuming a sibling parameter.
- A lower-level raw output signature for managed implementers.

This limitation should not be used to distinguish the two proposals, but it must bound claims about managed COM server support.

## Unique COM ownership

Unique versus identity-cached COM ownership requires caller intent and is orthogonal to COM-versus-WinRT detection.

The policy prototype proved that `UniqueComInterfaceMarshaller<T>` can provide deterministic independent release. Neither core proposal requires exposing that feature now.

The decision options are:

- Omit unique ownership from this work.
- Add a separate friendly API later.
- Extend the caller-selected enum later with `ComObjectUniqueInstance`.

## Decision guidance

Prefer the caller-selected proposal if:

- Avoiding an extra QI on ordinary COM output is a hard requirement.
- Preserving existing wrapper identity is more important than generated simplicity.
- Callers can reliably express WinRT intent at the call site.
- The raw companion and adapter complexity is acceptable.

Prefer the adaptive proposal if:

- Correct behavior for `object` and later casts should be automatic.
- One symmetric COM interface declaration is a priority.
- One QI per relevant output is acceptable.
- Inspectable COM objects being represented by C#/WinRT wrappers is acceptable.
- C#/WinRT dynamic interface casting can be required.

## Evidence still needed

The policy architecture has broad generator and runtime coverage in draft PR #1771. The adaptive architecture currently has an isolated runtime and Native AOT proof.

Before selecting the adaptive proposal, it should be integrated far enough to measure:

- Generated source and compile-time impact.
- Full generator test behavior.
- Performance of successful and failed `QI(IInspectable)` paths.
- Behavior with proxies, disconnected objects, and nonconforming QI implementations.
- Diagnostics when dynamic interface casting is disabled.

Before selecting the policy proposal, the explicit `WindowsRuntime` plus generated COM `T` path should be wired into the policy prototype and validated end to end: request the COM IID, query the returned pointer for `IInspectable`, project the C#/WinRT wrapper, and dynamically cast it back to the generated COM interface.

## Decision questions

1. Is avoiding one `QI(IInspectable)` on ordinary COM output a hard requirement or an optimization preference?
2. Should `out object` automatically remain castable to WinRT interfaces?
3. Is wrapper identity chosen by runtime capability acceptable?
4. Is the same-IID companion and managed receiver adapter acceptable generated complexity?
5. Can C#/WinRT dynamic interface casting be a required runtime setting?
6. Should unique COM ownership be omitted from the initial decision?
