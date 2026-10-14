# Metadata annotations and compatibility

Native metadata signatures need not replace native integers or pointers with synthetic enum or handle types.
CsWin32 consumes annotations in `Windows.Win32.Foundation.Metadata` to recover logical APIs without inventing native representation or ownership facts.

## Associated enums

`AssociatedEnumAttribute(string Name)` on a field, parameter, or return value identifies a logical enum.
An unqualified name first prefers the declaring namespace; otherwise it must be unambiguous in the metadata's loaded assembly-reference closure.
A namespace-qualified name can identify an enum in a referenced metadata assembly.
Loading an unrelated metadata file does not make its enum eligible.
Missing, ambiguous, non-enum, or platform-incompatible targets produce generation errors.

When the native primitive and enum underlying type have exactly the same width **and signedness**, CsWin32 projects the enum directly.
This includes scalar fields, pointer elements and depth, fixed/flexible buffers, and callback signatures.
Exact-storage fields remain real fields, so callers can continue taking `ref` references.
Enum members, underlying types, and `[Flags]` semantics come from the enum definition.

Earlier CsWin32 projections of already-annotated primitive fields used enum properties even for identical storage.
Those members now become fields: normal reads and assignments remain source compatible, but property-specific contracts are not equivalent.

Unequal scalar storage requires conversion rather than reinterpretation.
Structs retain a native backing field and expose a converting enum property.
Runtime or source-generated scalar marshaling preserves the native integral type.
Without marshaling, extern methods and unmanaged COM structs retain native signatures and expose converting enum overloads.
A return-only conversion uses an `_Enum` suffix when parameter types cannot distinguish the overload.
Conversions are unchecked, including in a checked caller, so a narrower native value retains its native truncation behavior.

Unmanaged pointer/array elements must have identical storage; changing their width or signedness is rejected.
Indirect unequal-storage conversions with source-generated marshaling are also rejected rather than dropping an element marshaler.
Unequal unmanaged callback conversions and combined `AssociatedEnum`/`NativeBitfield` backing fields are unsupported and produce generation errors.
These restrictions do not affect unannotated metadata.

An unavoidable source boundary is an unequal-storage field: its enum view is a property, not a ref-returnable field.
A caller that previously passed such a fabricated enum field by reference must copy the property to a local, pass that local, and assign it back if modified.
Retaining the old field layout would instead misrepresent the native ABI.

## Contextual ownership

`RAIIFreeAttribute(string Name)` on a producing return value or output parameter identifies cleanup.
Return annotations occupy metadata parameter row sequence zero.
`InvalidHandleValueAttribute(long Value)` may specify multiple invalid values on that return/output row.
Explicit contextual invalid values override legacy typedef invalid values.

Generated owner identity includes the cleanup function, native value representation, and invalid-value set.
Different invalid-value contracts cannot share an owner with an incorrect `IsInvalid`.
A nondefault contract adds a deterministic `Invalid...` suffix to the owner type name.
BCL `SafeFileHandle`/`SafeRegistryHandle` reuse requires a compatible explicit contract: both zero and minus one are invalid.
Absent contextual values retain existing behavior.

Typed borrowing leases and protected dangerous-value access retain their lifetime safeguards.
CsWin32 does not infer a fabricated handle identity or cleanup contract merely from a `void*` signature.
In particular, the supplied WinHttp pilot has no HINTERNET ownership annotations: its raw `void*` APIs remain raw, not automatically owned.
Wider or otherwise unsupported ownership representations require explicit review rather than guessed handle metadata.

## Pinned consumer review

The baseline is the in-progress ownership implementation at
[`f3f5a69aa4823466ef7b55bbdc8e5ba41ec021e6`](https://github.com/microsoft/CsWin32/commit/f3f5a69aa4823466ef7b55bbdc8e5ba41ec021e6)
from [#1844](https://github.com/microsoft/CsWin32/pull/1844), **not a released CsWin32 baseline**.
No release-compatibility claim follows from these checks.

`HeaderAnnotationCompatibilityTests` runs OLD metadata + baseline CsWin32, OLD metadata + proposed CsWin32, and NEW metadata + proposed CsWin32.
All legs use the same requested API list, reference compilation, generation options, and unchanged caller source.
The pinned context is `net8.0-windows`, x64, unsafe, C# 12, Roslyn 5.9.0, public single-file generation, SafeHandles enabled, and separate `allowMarshaling=true`/`false` legs.
The matrix uses runtime marshaling, not COM source generators.

The harness compares public symbols as well as compiling unchanged callers.
Reports grade results as `identical-generation`, `source-compatible-difference`, or `residual-source-break`, and retain symbol additions/removals and exact caller diagnostics.
Mutation controls remove associations or change pointer depth and require the unchanged canonical caller to fail.
An existing-annotation fixture exercises both a source-compatible field/property difference and a failing unchanged property-contract caller, so all three grades have concrete coverage.

### Recorded checkpoint evidence

| Metadata / generator | Marshaling / no-marshaling public symbols | Result |
|--|--|--|
| Canonical OLD / baseline | 19 / 18 | Unchanged callers compile |
| Canonical OLD / proposed | 19 / 18 | Identical generation and public symbols |
| Canonical NEW / proposed | 19 / 18 | Identical generation and public symbols |
| WinHttp OLD / baseline | 187 / 186 | Unchanged callers compile |
| WinHttp OLD / proposed | 187 / 186 | Identical generation and public symbols |
| WinHttp NEW / proposed | Pending | Full NEW image has not been supplied |

`ExistingAnnotatedFieldMatrix` records the intentional member-kind change from
`public MODE Windows.Win32.Test.RECORD.Mode { get; set; }` to a `public MODE` field.
The same ordinary reads and assignments compile with both generators.
An unchanged caller extension `public partial struct RECORD : IModeProperty`, where `IModeProperty` requires a `MODE Mode { get; set; }` property, instead fails with `CS0535`.
The verified smallest adjustment is an explicit forwarding implementation:
`MODE IModeProperty.Mode { get => this.Mode; set => this.Mode = value; }`.
Reflection-based callers looking up a property likewise need to account for the field representation; reflection behavior is not covered by the compile matrix.
This is an observed canonical source break, distinct from the pending WinHttp findings.

Canonical metadata is emitted deterministically from the checked-in `AssociatedEnumTests` declarations:

| Snapshot | SHA256 |
|--|--|
| Canonical OLD | `BC8F4A7A0E56B19143EA06D205C10C132E47CB3F5F60AA194A0D7A17D8F41B11` |
| Canonical NEW | `579388A069AAE858D676E619527D5983280916F30CD3316AD85377F075FBD227` |
| WinHttp OLD full image | `1361407C2888102CB5161D83503235C09C36F917837C7107CA0A80E5CF4FAC43` |

The WinHttp image is from producer commit `b5b7b26ed09d648766f07449af48c64ef0e99d13`, native/header mirror `77817f12308e29ec8bf77dc6fe56786e65ff9e08`, SDK `10.0.26100.7705`, and matched ClangSharp `21.1.8.4`.
The requested WinHttp corpus is checked into `HeaderAnnotationCompatibilityTests.WinHttpThreeWayMatrix`.
It covers request/access/authentication/scheme/URL flags, ref fields, raw and friendly entry points, callbacks, read buffers, interior-pointer header buffers, raw creation, and cleanup.
Canonical ownership tests separately execute contextual invalid values, output ownership, disposal while borrowed, and lease release, and run the lifetime analyzer.

The NEW WinHttp leg is **not accepted at this checkpoint**.
The producer identified a possible rename of the four `WINHTTP_INTERNET_SCHEME` members:
`WINHTTP_INTERNET_SCHEME_HTTP`, `_HTTPS`, `_FTP`, and `_SOCKS` to the corresponding native `INTERNET_SCHEME_*` names.
This is a predicted source break, not an observed result without the NEW image.
The harness records exact diagnostics at the unchanged `EnumFields` call sites and checks that changing only those member names restores compilation.
Other NEW diagnostics fail the acceptance check rather than being waived.

All eight legal enum storage types are additionally checked on x86, x64, and ARM64.
Source-generated scalar-conversion tests inspect generated conversion types and return attributes; they do not run the platform LibraryImport generator or native marshaling.
This checkpoint therefore does not establish runtime acceptance of source-generated conversion marshalers.

### Reproducing the matrix

From a restored checkout, run:

```powershell
.\tools\Test-HeaderAnnotationCompatibility.ps1
```

The runner archives and builds the exact baseline commit under this worktree's ignored `obj` directory.
It sets `NBGV_GitEngine=Disabled` for its .NET commands and does not change package feeds or versions.
No other checkout or branch is modified.

Supply immutable SDK images and their SHA256s for the pilot:

```powershell
.\tools\Test-HeaderAnnotationCompatibility.ps1 `
    -OldMetadata 'C:\snapshots\old\Windows.Win32.winmd' -OldSHA256 '<OLD SHA256>' `
    -NewMetadata 'C:\snapshots\new\Windows.Win32.winmd' -NewSHA256 '<NEW SHA256>'
```

OLD-only input checks and reports the baseline/proposed OLD pair, then explicitly skips NEW acceptance.
Reports and generated sources are written to `obj\HeaderAnnotationCompatibility`.
JSON records the exact baseline/proposed commits, built generator assembly hashes, metadata hashes, options, requested APIs, unchanged callers, public symbols, and diagnostics.
A dirty proposed checkout is explicitly marked `+working-tree`.
