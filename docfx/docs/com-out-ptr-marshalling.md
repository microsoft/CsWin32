# Design note: policy-based COM output pointer marshalling

## Status

Proposed.

This note describes a design for friendly overloads over native COM output pointers. It is intended to guide a prototype and collect feedback before the API is considered final.

## Summary

CsWin32 should preserve its existing generic friendly overloads for methods with an `IID`/`void**` pair and add a policy-bearing overload:

```csharp
public enum ComOutPtrMarshalling
{
    Default,
    ComObject,
    WindowsRuntime,
    ComObjectUniqueInstance,
}
```

```csharp
// Existing overload remains for source and binary compatibility.
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);

// New overload makes an otherwise ambiguous policy explicit.
shellItem.BindToHandler<object>(
    null,
    bhidStorageItem,
    out object storageItem,
    ComOutPtrMarshalling.WindowsRuntime);
```

The existing overload forwards to the new overload with `Default`.

`Default` is type-directed:

- A C#/WinRT projected interface uses `WindowsRuntime`.
- `object` and a source-generated COM interface use `ComObject`.

The source generator cannot know the call site's closed generic `T`. The generated method therefore classifies `typeof(T)` once per closed generic instantiation and caches the result. This is static with respect to the managed type, but it is not source-generation-time specialization. It does not probe the returned object and therefore does not add a `QueryInterface(IInspectable)` operation to ordinary COM calls. Callers use an explicit enum value when the desired wrapper family cannot be inferred from `T`, most importantly when requesting `out object`.

The friendly overload should receive the raw ABI pointer and select the managed projection after the native call. The underlying public P/Invoke and COM interface declarations remain unchanged for compatibility.

## Motivation

CsWin32 generates generic friendly overloads for methods that follow the `IID_PPV_ARGS` pattern:

```csharp
shellItem.BindToHandler<IStream>(
    null,
    bhidStream,
    out IStream stream);
```

With source-generated COM, the current overload projects the returned pointer through `ComInterfaceMarshaller<T>`. This works for CsWin32-generated COM interfaces, but it does not work for C#/WinRT interfaces:

```csharp
shellItem.BindToHandler<IStorageItem>(
    null,
    bhidStorageItem,
    out IStorageItem storageItem);
```

The generated COM marshaller creates a `System.Runtime.InteropServices.Marshalling.ComObject`. That wrapper cannot be cast to the C#/WinRT projection `Windows.Storage.IStorageItem`.

The opposite problem also exists for ownership. `ComInterfaceMarshaller<T>` uses the normal identity cache. Some callers instead need `UniqueComInterfaceMarshaller<T>` so they can deterministically release a wrapper without affecting other managed references to the same COM identity.

A method allowlist cannot select the correct behavior. The same native method can return a native COM interface, a Windows Runtime interface, or an object whose ownership must be isolated. The choice belongs to the call.

## Goals

- Correctly project C#/WinRT interfaces returned from `IID`/`void**` methods.
- Preserve existing native COM behavior and cost.
- Allow callers to request an identity-cached or unique source-generated COM wrapper.
- Make `Default` choose from the closed managed type rather than inspecting the returned object.
- Allow an explicit policy when `T` does not express the desired wrapper family.
- Preserve existing generated method signatures.
- Preserve the managed signature implemented by `[GeneratedComInterface]` implementers.
- Support Native AOT.
- Balance every native reference on success and failure.

## Non-goals

- Detect a future cast after a value has escaped as `object`.
- Probe every returned object for `IInspectable`.
- Make one managed wrapper simultaneously implement arbitrary CsWin32 and C#/WinRT interface projections.
- Change input parameter marshalling.
- Apply the policy to COM interface return values or arrays of COM pointers.
- Apply the first implementation to every fixed-type COM output parameter.
- Change built-in COM or no-marshalling projections in the first implementation.

## Initial support

The first implementation targets:

- `allowMarshaling: true`.
- `comInterop.useComSourceGenerators: true`.
- `comInterop.useIntPtrForComOutPointers: false`.
- Build-task mode (`CsWin32RunAsBuildTask=true`), which allows generated `[LibraryImport]` declarations to be processed in the consuming compilation.

Built-in COM and no-marshalling generation retain their current overloads. The raw ABI architecture may make future Windows Runtime support possible in those modes, but `ComObjectUniqueInstance` is specifically implemented by `UniqueComInterfaceMarshaller<T>` in source-generated COM mode.

## Proposed API

### Generated enum

CsWin32 generates the enum once when a policy-bearing overload is emitted:

```csharp
namespace Windows.Win32;

public enum ComOutPtrMarshalling
{
    Default = 0,
    ComObject = 1,
    WindowsRuntime = 2,
    ComObjectUniqueInstance = 3,
}
```

The enum follows the configured visibility of generated APIs.

### Compatibility overload

The existing signature remains:

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv)
    where T : class;
```

Its implementation becomes equivalent to:

```csharp
@this.BindToHandler(
    pbc,
    bhid,
    out ppv,
    ComOutPtrMarshalling.Default);
```

Keeping this overload is important even though CsWin32 emits source. Generated APIs may be compiled into a public library and consumed by already-built assemblies. Replacing the method with a signature that only differs by an optional parameter would be a binary breaking change.

### Policy-bearing overload

The new overload appends a required policy parameter:

```csharp
public static void BindToHandler<T>(
    this IShellItem @this,
    IBindCtx? pbc,
    in Guid bhid,
    out T ppv,
    ComOutPtrMarshalling marshalling)
    where T : class;
```

The parameter is required on this overload. `Default` is used by the compatibility overload and is also available to callers that select the policy-bearing overload explicitly.

## Policy semantics

| Policy | Supported `T` | Requested IID | Managed projection |
| --- | --- | --- | --- |
| `Default` | `object`, generated COM interface, or projected WinRT interface | Selected from the resolved policy | Selected from the resolved policy |
| `ComObject` | `object` or generated COM interface | `IID_IUnknown` for `object`; otherwise `typeof(T).GUID` | `ComInterfaceMarshaller<T>` |
| `WindowsRuntime` | `object` or projected WinRT interface | `IID_IInspectable` for `object`; otherwise `WinRT.GuidGenerator.CreateIID(typeof(T))` | `MarshalInspectable<object>` for `object`; otherwise `MarshalInterface<T>` |
| `ComObjectUniqueInstance` | `object` or generated COM interface | `IID_IUnknown` for `object`; otherwise `typeof(T).GUID` | `UniqueComInterfaceMarshaller<T>` |

Invalid combinations fail before invoking native code:

- `ComObject` or `ComObjectUniqueInstance` with a projected WinRT interface.
- `WindowsRuntime` with a CsWin32-generated COM interface.
- A runtime class or other non-interface type, except for `object`.
- `WindowsRuntime` when C#/WinRT support is not referenced.
- `ComObjectUniqueInstance` when source-generated COM is unavailable.

`WindowsRuntime` with a CsWin32-generated COM interface cannot produce a value assignable to `T`. A C#/WinRT wrapper can expose that native IID, but it does not implement an unrelated managed `[GeneratedComInterface]` type. Code that wants a Windows Runtime wrapper for later casting should use `out object` and explicitly select `WindowsRuntime`, or request the projected WinRT interface as `T`.

Some APIs interpret the requested IID as more than a final `QueryInterface`. For those APIs, `out object` requests `IID_IUnknown` or `IID_IInspectable` and may not be equivalent to requesting the eventual interface IID. The projected interface should be used as `T` whenever the API requires a specific IID.

## Type-directed `Default`

The generic friendly method is emitted before call sites are bound, so CsWin32 cannot select a projection at source-generation time for each eventual `T`. The closest type-static behavior is a generic static cache initialized from `typeof(T)`. This distinction should be explicit in API documentation.

When C#/WinRT is referenced, CsWin32 uses its type classifier:

```csharp
WinRT.Projections.IsTypeWindowsRuntimeType(typeof(T))
```

This correctly recognizes:

- Projected Windows Runtime interfaces.
- Mapped BCL interfaces such as `IReadOnlyList<string>`.
- Parameterized Windows Runtime interfaces whose IID is not `typeof(T).GUID`.

The result should be cached once per closed generic `T`. This is a managed type classification; it does not inspect the returned native object and does not call `QueryInterface(IInspectable)`.

`object` deliberately resolves to `ComObject`. The managed type contains no evidence that the caller wants a Windows Runtime wrapper. The caller must select `WindowsRuntime` explicitly.

When C#/WinRT is not referenced, `Default` resolves to `ComObject`. CsWin32 should emit a diagnostic where it can prove that `WindowsRuntime` was selected without the required reference, and the generated fallback should throw before the native call.

The generic type parameter must carry the trimming annotations required by C#/WinRT IID generation. Native AOT coverage is required for projected and mapped interfaces.

## Raw ABI requirement

The policy must be applied to the pointer returned by native code.

Applying `ComInterfaceMarshaller<object>` first is insufficient:

- It creates an identity-cached `ComObject` before a unique wrapper can be requested.
- It creates the wrong wrapper family for C#/WinRT.
- Converting that intermediate wrapper back to ABI adds work and leaves an unnecessary managed wrapper in the identity cache.

The policy-bearing overload therefore invokes a raw ABI path, then projects the pointer exactly once.

The public generated declaration should not be changed merely to provide that raw path. Direct callers may rely on its existing managed signature.

## Flat P/Invoke methods

For a flat Win32 method, CsWin32 should emit a private raw companion that shares the native entry point and calling convention:

```csharp
[LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName")]
private static partial HRESULT SHCreateItemFromParsingName__ComOutPtr(
    string pszPath,
    IBindCtx? pbc,
    in Guid riid,
    out nint ppv);
```

The companion keeps the source-generated marshalling for ordinary inputs and exposes only the COM output pointer as `nint`. The existing public declaration remains unchanged. Only the friendly overload calls the raw companion.

This changes the generated implementation path, not the native ABI and not the public raw API.

The private `[LibraryImport]` companion requires build-task mode. In ordinary analyzer/source-generator mode, one source generator cannot process another generator's output, so a generated partial `[LibraryImport]` method would not receive an implementation.

## COM interface methods

### Preserve the public interface declaration

The generated interface should retain its existing method:

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

Changing this method to `void**` would force every managed implementer to handle an unsafe ABI pointer. Adding the managed-only policy parameter to the interface would also be incorrect because it is not part of the native ABI.

### Private same-IID raw companion

CsWin32 should generate an internal companion interface with the same IID and vtable layout as the public interface, but with policy-relevant outputs exposed as `nint`:

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

The companion preserves managed marshalling for every other parameter. When the receiver is already an RCW, the policy-bearing extension uses its dynamic interface cast to obtain the companion, invokes the same COM slot, and projects the returned `nint`.

A direct managed implementation of the public interface does not implement the private companion, so that cast alone would break the historical ability to call a friendly overload directly on a managed object. For that case, the extension temporarily obtains the public interface's CCW pointer with `ComInterfaceMarshaller<TPublic>.ConvertToUnmanaged`, projects a unique raw-companion RCW over the same IID, invokes it, and deterministically releases both temporary references. Existing RCWs stay on the direct fast path.

Two `[GeneratedComInterface]` types with the same IID are supported because source-generated COM binds projection behavior to the managed interface type rather than a process-wide IID-to-type registration. A prototype successfully:

- Cast a native `IShellItem` RCW to a same-IID raw companion.
- Passed a managed `IBindCtx` input while receiving `out nint`.
- Invoked a managed `[GeneratedComClass]` implementation of the normal interface through the raw companion.
- Invoked the compatibility overload directly on a managed `[GeneratedComClass]` implementation.
- Ran on .NET 9 and .NET 10 without compiler diagnostics.

The companion must preserve the complete inherited and declared vtable layout through the target method. Mirroring the interface hierarchy is safer than calculating and invoking function-pointer slots in each friendly overload.

The companion is an RCW consumption detail. CsWin32-generated managed classes must not implement both the public interface and its same-IID companion because that would place duplicate IID entries on one CCW.

### Managed interface implementers

The original managed interface remains unchanged, so a managed implementation still writes an `object`:

```csharp
public void BindToHandler(..., out object value)
{
    value = storageFile;
}
```

The `[GeneratedComInterface]` CCW marshals that object back to an ABI pointer. `ComInterfaceMarshaller<object>` already recognizes an existing C#/WinRT wrapper through `ComWrappers.TryGetComInstance`; no WinRT-specific input marshaller is required.

The CCW does not understand the adjacent `riid` convention. For an `out object` parameter it may return the object's `IUnknown` without first querying the requested IID. The caller-side projection must therefore tolerate a bare `IUnknown` and perform the QI needed for `T` or `IInspectable`. This differs from a native implementation that honors `riid` and returns the requested interface pointer.

The caller-side friendly overload then chooses the requested projection. `ComObjectUniqueInstance` is likewise a caller-side ownership choice. The implementer does not need to know which policy the caller selected.

Calling the friendly overload directly on the managed implementation uses the temporary CCW/raw-RCW adapter described above; calling through an existing COM proxy uses the raw companion directly. Both paths must be covered by managed implementation tests, not only by calls to native shell objects.

## Why not put a custom marshaller on the public COM interface?

`[MarshalUsing]` is attractive because `[GeneratedComInterface]` uses the same declaration for RCW and CCW generation. It does not solve the call-site policy problem on the existing signature:

- The interface method is not generic, so its marshaller cannot observe the friendly overload's `T`.
- The enum is a parameter of the friendly overload, not of the native interface method.
- An `out object` marshaller has no initialized managed value from which to read policy before the call.
- Replacing `out object` with a policy carrier would change the method implemented by managed COM servers.
- An adaptive marshaller that probes `IInspectable` would add the QI and behavior change that this design intentionally avoids.

The private same-IID companion avoids a policy-aware custom marshaller: `out nint` uses built-in blittable marshalling, while the source-generated COM stub continues to marshal all other parameters. A custom marshaller may still reduce duplication for other output shapes, but it is not required for the IID/output design.

## Projection and ownership

The raw output pointer owns one native reference.

### Identity-cached COM

```csharp
T value = ComInterfaceMarshaller<T>.ConvertToManaged(native);
ComInterfaceMarshaller<T>.Free(native);
```

### Unique COM

```csharp
T value = UniqueComInterfaceMarshaller<T>.ConvertToManaged(native);
UniqueComInterfaceMarshaller<T>.Free(native);
```

The resulting wrapper uses `CreateObjectFlags.UniqueInstance`. Callers that need deterministic release can use `ComObject.FinalRelease()` on the returned wrapper without releasing another identity-cached wrapper.

If the pointer unwraps to an existing managed object, the marshaller may return that managed object rather than a unique `ComObject`; deterministic `FinalRelease()` applies to genuine native COM wrappers.

### Windows Runtime

```csharp
T value = WinRT.MarshalInterface<T>.FromAbi((nint)native);
WinRT.MarshalInterface<T>.DisposeAbi((nint)native);
```

For `object`, use `WinRT.MarshalInspectable<object>`.

Cleanup occurs in `finally`, including when projection throws. Failed HRESULT paths do not attempt to project a null or invalid result.

## Multiple IID/output pairs

The SDK metadata contains:

- 420 generator-relevant methods with exactly one canonical adjacent IID/output pair.
- One method with two pairs:
  `ID3D12SwapChainAssistant.GetCurrentResourceAndCommandQueue`.
- 15 single-pair methods where the pair is not the final two parameters.

The current implementation scans only the final two parameters. The prototype should scan the entire parameter list and associate each `[ComOutPtr] void**` with its immediately preceding `Guid*`.

For the one multi-pair method, the policy-bearing overload should expose one type parameter and one policy for each pair:

```csharp
GetCurrentResourceAndCommandQueue<TResource, TQueue>(
    out TResource resource,
    out TQueue queue,
    ComOutPtrMarshalling resourceMarshalling,
    ComOutPtrMarshalling queueMarshalling);
```

Each output is projected independently and each returned reference is cleaned up independently.

The broader metadata set contains 96 methods with multiple fixed-type COM outputs: 87 with two and 9 with three. Applying this policy to all fixed-type COM outputs would add a large generated API surface. The first implementation should remain scoped to IID/output generic overloads. The raw projection helper can be reused if a later proposal generalizes unique-instance selection to fixed-type outputs.

## Source and binary compatibility

- Existing generic overload signatures remain present.
- Existing calls resolve to the compatibility overload and use `Default`.
- Native COM `T` continues to use the identity-cached source-generated COM projection.
- A projected WinRT `T` changes from a failing cast to the C#/WinRT projection.
- The existing public P/Invoke and COM interface declarations remain unchanged.
- Managed `[GeneratedComInterface]` implementations retain their existing method signatures.
- The generated enum and policy-bearing overload are additive.
- Projects without a C#/WinRT reference do not gain a mandatory C#/WinRT dependency.

## Diagnostics

The generated method must validate unsupported combinations before invoking native code. An analyzer can provide earlier and more actionable diagnostics for:

- `ComObject` or `ComObjectUniqueInstance` with a projected WinRT `T`.
- `WindowsRuntime` with a generated COM interface `T`.
- Runtime classes and other unsupported `T` values.
- `WindowsRuntime` without C#/WinRT.
- `ComObjectUniqueInstance` without source-generated COM.

An analyzer cannot generally infer that an `object` will later be cast to a WinRT interface after it escapes. Explicit `WindowsRuntime` remains the correctness mechanism for that case.

## Prototype plan

1. Replace the named `AsWinRT<T>` prototype with `ComOutPtrMarshalling`.
2. Generate the enum and compatibility/policy overload pair.
3. Add a shared raw-pointer projection helper.
4. Add a private raw companion for flat P/Invokes.
5. Add internal same-IID raw companion interfaces without changing the public interface declaration.
6. Expand IID/output pair discovery beyond the final two parameters.
7. Validate a managed `[GeneratedComInterface]` implementation returning a C#/WinRT object.
8. Measure generated source size and compile-time impact.

## Required validation

- `Default` + native COM interface.
- `Default` + projected WinRT interface.
- `Default` + mapped parameterized interface such as `IReadOnlyList<string>`.
- `Default` + `object`.
- Explicit `WindowsRuntime` + `object`.
- Explicit `ComObject` + `object`.
- Explicit `ComObjectUniqueInstance`, including deterministic release.
- Invalid policy/type combinations fail before native invocation.
- Flat P/Invoke and COM interface methods.
- Native COM server and managed `[GeneratedComInterface]` implementer.
- Managed implementer returning bare `IUnknown`, followed by successful `ComObject` and `WindowsRuntime` projection.
- PreserveSig and exception-translated COM methods.
- Failure with null and non-null output pointers.
- A method whose IID/output pair is not final.
- The method with two IID/output pairs and mixed policies.
- No C#/WinRT reference.
- Native AOT publish and execution.
- Existing non-policy COM runtime tests.
- Full generator test suite.

## Alternatives considered

### Always probe for `IInspectable`

This handles values received as `object`, but it adds a QI to every COM output and changes wrapper selection based on the returned object rather than caller intent.

### Infer only from `T`

This is the `Default` behavior, but it cannot handle `out object`. The explicit enum values are still required.

### Generate separate `AsWinRT<T>` and `AsUnique<T>` methods

Named methods are clear but multiply overloads as policies and multiple output pointers are added.

### Configure methods in `NativeMethods.json`

A method-level opt-in cannot express that one call returns native COM while another call to the same method returns WinRT. It also moves a call-site decision into project-wide configuration.

### Change public interop declarations to raw pointers

This makes policy application straightforward but breaks direct callers and forces managed COM implementers to work at the ABI level.

### Put an adaptive custom marshaller on `out object`

The marshaller cannot observe `T` or the friendly overload's policy. Probing the result would impose the same cost and ambiguity as always probing for `IInspectable`.

## Review questions

1. Is preserving the existing generic overload as a forwarding compatibility shim worth the additional overload?
2. Should explicit `WindowsRuntime` support only `object` and projected WinRT interfaces, as proposed?
3. Should missing C#/WinRT support be a generated runtime guard, an analyzer error, or both?
4. Are internal same-IID raw companion interfaces acceptable as an implementation detail?
5. Should fixed-type COM outputs be considered in this change or remain a follow-up?
