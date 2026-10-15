# Associated enums

Win32 metadata can annotate a native integer, pointer or buffer with
`Windows.Win32.Foundation.Metadata.AssociatedEnumAttribute(string Name)`.
CsWin32 uses the named enum in the generated API when its declared underlying type
matches the native value's width and signedness.

For example, an enum declared as `MODE : uint` can represent an annotated native
`uint` parameter or the elements of an annotated `uint*` buffer:

```csharp
public enum MODE : uint { First = 1, Second = 2 }

// Native metadata:
public static extern void SetMode([AssociatedEnum("MODE")] uint mode);
public static extern unsafe void ReadModes(
    [In, AssociatedEnum("MODE"), NativeArrayInfo(CountParamIndex = 1)] uint* modes,
    uint count);

// Generated signatures, with import attributes omitted:
public static extern void SetMode(MODE mode);
public static extern unsafe void ReadModes(MODE* modes, uint count);
public static unsafe void ReadModes(ReadOnlySpan<MODE> modes);
```

The enum is ABI-compatible with the native value: no scalar conversion or enum
marshaler is required. Its underlying type, member names, values and `[Flags]`
attribute remain as declared in metadata.
This applies with runtime marshaling enabled or disabled, including return
parameters, callbacks, multiple pointer levels, and fixed or flexible buffers.

## Fields

Annotated scalar fields retain the existing native-backed enum-property shape:

```csharp
private uint _Mode;

public MODE Mode
{
    get => (MODE)this._Mode;
    set => this._Mode = (uint)value;
}
```

This preserves callers that implement interface properties or use property reflection.
Pointer and buffer fields expose enum elements directly when their storage matches.

## Resolving enum names

An unqualified name such as `MODE` prefers the annotated member's declaring namespace.
Otherwise, the name must identify exactly one type in that metadata assembly and
its loaded assembly-reference closure.
A qualified name such as `Windows.Win32.Test.MODE` selects that namespace explicitly.
An unrelated loaded metadata assembly is not searched.
Missing, ambiguous, non-enum, or platform-incompatible targets produce generation errors.

## Incompatible metadata

If the native value and enum have different underlying types, CsWin32 reports
warning **PInvoke015** and ignores that association.
The native parameter, pointer, buffer or field representation is preserved.
For example, `uint*` cannot be projected as a pointer to an enum declared with
`ushort` or `int` storage.
The same warning applies when an existing C# enum has incompatible storage, the
annotated native shape is not integral, or a field is also a `NativeBitfield` backing field.

CsWin32 does not change an enum's underlying type to compensate for inconsistent
metadata, generate converting enum overloads for it, or reinterpret incompatible buffers.
Correct the annotation or enum definition in the metadata.

This also applies to existing annotations in the Windows metadata packages.
For example, the currently shipped annotations for `SHObjectProperties`,
`PathCleanupSpec`, `IShellFolderView.Select`, and `SHDESCRIPTIONID.dwDescriptionId`
disagree with their associated enums' signedness.
These APIs now retain their native integer types and produce PInvoke015 instead
of exposing a converting enum view.
Callers of affected APIs must use native integers or explicit enum casts until
the metadata is corrected.
