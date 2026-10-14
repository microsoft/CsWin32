# Metadata annotations and compatibility

Native metadata signatures need not replace native integers or pointers with synthetic enum or handle types.
CsWin32 consumes annotations in `Windows.Win32.Foundation.Metadata` to recover logical APIs without inventing native representation or ownership facts.

## Associated enums

`AssociatedEnumAttribute(string Name)` on a field, parameter, or return value identifies a logical enum.
An unqualified name first prefers the declaring namespace; otherwise it must be unambiguous in the metadata's loaded assembly-reference closure.
A namespace-qualified name can identify an enum in a referenced metadata assembly.
Loading an unrelated metadata file does not make its enum eligible.
Missing, ambiguous, non-enum, or platform-incompatible targets produce generation errors.

For a logical enum, **the native use site's primitive type determines the generated enum's underlying width and signedness**, not the logical enum's metadata `value__` type.
CsWin32 then references that enum directly in public signatures and fields.
This recovers the OLD synthetic-enum projection while the winmd keeps faithful native primitive/pointer signatures.
It includes scalars, pointer elements and depth, fixed/flexible buffers, callback signatures, and source-generated signatures.
Fields remain real, ref-returnable fields; they do not become conversion properties just because the logical metadata enum declared different storage.
Member names and `[Flags]` semantics still come from the enum definition.

Storage discovery considers compatible association sites in the actual loaded reference closure, including when the enum is requested before its consumers.
Conflicting native widths or signedness for one logical enum fail explicitly instead of depending on generation order.
Member values must fit the native width without truncation; representable signed/unsigned bit patterns, including high-bit flags, are preserved.
An already available C# enum must also have compatible storage.
Nonintegral storage, unsupported native shapes, and combined `AssociatedEnum`/`NativeBitfield` backing fields produce generation errors, not silent native-only fallbacks.

A distinct pre-existing case is an enum used **directly in native metadata signatures**, not just named by annotations.
Those signatures already constrain its native ABI, so annotations elsewhere cannot retype it.
An existing scalar field with conflicting storage retains the established native-backing-field/value-property pattern.
Extern methods and unmanaged COM structs retain raw scalar entry points and add explicit converting enum overloads; a return-only view uses an `_Enum` suffix.
Indirect or unsupported contexts with that genuine storage conflict fail explicitly.
These bounded compatibility paths do not change the direct-field/signature contract for migrated logical enums, add a legacy-property option, or infer caller interfaces.

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
The native-storage matrix deliberately gives the NEW logical enum a different metadata underlying type, across all eight integral native types and both enum-first/consumer-first request orders.
Public symbols and the same unchanged callers must still match the OLD synthetic-enum projection.
Independent existing-annotation regression reports are labeled separately from `synthetic-enum-migration` acceptance.

### Recorded checkpoint evidence

| Metadata / generator | Marshaling / no-marshaling public symbols | Result |
|--|--|--|
| Canonical OLD / baseline | 19 / 18 | Unchanged callers compile |
| Canonical OLD / proposed | 19 / 18 | Identical generation and public symbols |
| Canonical NEW / proposed | 19 / 18 | Identical generation and public symbols |
| Native-storage OLD / baseline and proposed, eight integral types | 19 / 18 | Identical generation; unchanged callers compile |
| Native-storage NEW / proposed, eight integral types, both request orders | 19 / 18 | Source-compatible text differences; identical public symbols and unchanged callers |
| WinHttp OLD / baseline | 187 / 186 | Unchanged callers compile |
| WinHttp OLD / proposed | 187 / 186 | Identical generation and public symbols |
| WinHttp NEW / proposed | 187 / 186 | Residual source break: four scheme-member renames; no other public-symbol or generated-text changes |

The actual WinHttp image runs pin proposed generator commit `41ffdb9f4cf445d224d4477ec674a4039308c724`.
All 41 compatibility cases ran without skips, followed by both canonical caller-mutation controls.
The runner now includes those controls in its 43-case selection.
OLD + proposed remains identical to OLD + baseline; NEW + proposed is not graded source-compatible merely because its explicitly adjusted caller compiles.

### Observed WinHttp member-name breaks

Both actual NEW marshaling legs report exactly four `CS0117` errors in the unchanged `EnumFields` caller.
The enum remains `WINHTTP_INTERNET_SCHEME : int`; `URL_COMPONENTS.nScheme` and `WINHTTP_PROXY_RESULT_ENTRY.ProxyScheme` remain direct enum fields, including ref-field access.
All other public symbols, method signatures, callback/buffer projections, raw `void*` ownership contracts, and generated text match OLD.

Each row identifies a public enum constant in `Windows.Win32.Networking.WinHttp.WINHTTP_INTERNET_SCHEME`, its unchanged caller, and the smallest caller adjustment:

| Removed member | Added member / same value | Failing unchanged call site |
|--|--|--|
| `WINHTTP_INTERNET_SCHEME_HTTP` | `INTERNET_SCHEME_HTTP = 1` | `UnchangedCaller.cs(16,47)`: `url.nScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_HTTP;` |
| `WINHTTP_INTERNET_SCHEME_HTTPS` | `INTERNET_SCHEME_HTTPS = 2` | `UnchangedCaller.cs(17,47)`: `url.nScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_HTTPS;` |
| `WINHTTP_INTERNET_SCHEME_FTP` | `INTERNET_SCHEME_FTP = 3` | `UnchangedCaller.cs(18,53)`: `entry.ProxyScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_FTP;` |
| `WINHTTP_INTERNET_SCHEME_SOCKS` | `INTERNET_SCHEME_SOCKS = 4` | `UnchangedCaller.cs(19,53)`: `entry.ProxyScheme = WINHTTP_INTERNET_SCHEME.WINHTTP_INTERNET_SCHEME_SOCKS;` |

The supplied candidate uses native macro spellings instead of the OLD synthetic member spellings and contains no compatibility aliases.
Changing only these four member references restores compilation in both marshaling modes.
That adjustment is tested and reported separately; the three primary legs keep their caller sources unchanged and NEW keeps its `residual-source-break` grade.
Exact added/removed symbols and a member-name-only generated-text comparison reject any additional surface or output changes.
These four observed source breaks remain explicit maintainer-review items, not skipped NEW coverage.
These measurements do not establish that the renames are unavoidable, decide whether OLD logical member spellings can be preserved, or imply pilot acceptance.

### Independent existing-annotation regression

`ExistingAnnotatedFieldRegression` separately records the member-kind change from
`public MODE Windows.Win32.Test.RECORD.Mode { get; set; }` to a `public MODE` field.
The same ordinary reads and assignments compile with both generators.
An unchanged caller extension `public partial struct RECORD : IModeProperty`, where `IModeProperty` requires a `MODE Mode { get; set; }` property, instead fails with `CS0535` at `UnchangedCaller.cs(21,36)`.
Reflection-based callers looking up a property likewise need to account for the field representation; reflection behavior is not covered by the compile matrix.
This is a separately demonstrated existing-consumer regression, **not the OLD synthetic-enum field contract**, a pilot policy decision, or a prerequisite for WinHttp acceptance.
No caller shim, interface inference, or new generation option is required by the migration contract.
The two-generator evidence is retained in `existing-annotation-*.json` with `CompatibilityScope=independent-existing-annotation-regression`; it is not mislabeled as a migrated NEW leg.

### Snapshot provenance and physical limitations

Canonical metadata is emitted deterministically from the checked-in `AssociatedEnumTests` declarations:

| Snapshot | SHA256 |
|--|--|
| Canonical OLD | `155C94BA94A497FE616BD9096799F3F86AB99B26EA82D3D91CB6608FC45522A0` |
| Canonical NEW | `6255F8A82C1FFED6DD6BF62BAB3162B7481EC2C49E5C4915899A6BC6A03A20F9` |
| WinHttp OLD full image | `1361407C2888102CB5161D83503235C09C36F917837C7107CA0A80E5CF4FAC43` |
| WinHttp migrated NEW full image | `C7AEC280B0CBB19CDAD6B94FFED8473B2B22E4042BF886FCF33DCBDD92274258` |
| Matching unmigrated frozen-input tooling control | `274A288E622B840552934AE74FE2A33FEC4E7D4E47E3D3454B92A147A13B0CDA` |

The frozen OLD WinHttp image is from producer commit `b5b7b26ed09d648766f07449af48c64ef0e99d13`, native/header mirror `77817f12308e29ec8bf77dc6fe56786e65ff9e08`, SDK `10.0.26100.7705`, and matched ClangSharp `21.1.8.4`.
This frozen OLD image has a confirmed pre-existing physical defect: 39 local metadata-attribute constructor MethodDefs lack `HASTHIS`, while instance MemberRefs are present.
For example, the `AssociatedEnumAttribute` and `RAIIFreeAttribute` constructor signatures are `0001010E`, and `InvalidHandleValueAttribute` is `0001010A`, without the instance bit.
The OLD generated-C# and unchanged-caller results remain valid **consumer projection checks**, but do not establish physically valid constructor binding for the OLD SDK image.
CsWin32's name/payload interpretation and compilation of generated source are not a full metadata binding validator.
This known tooling-baseline defect is not attributed to the header migration, and the frozen OLD snapshot is not replaced.

The actual migrated NEW image is from producer commit `9faf8a24831e1ef0a44d4ccfe4cd972f380452b3`, with tooling ancestor `00ade0afc94c965522ccdbac54a06fc1bcbd01a1`, the same SDK CPP, and the unchanged matched ClangSharp stack.
The tooling ancestor includes `e065daa65c51769815e4a37c229ec9191a0578f4` (SAL/MIDL `RetVal` deduplication), `461d84f61c6c9611985a1152addf4b4636c5afc4` (local instance-constructor `HASTHIS`), and native-association/discovery-cache corrections.
The matching complete **unmigrated frozen-input tooling control** is pinned separately in the table above.
Our limited constructor-header audit finds no missing instance bits in NEW or that control.
It is not migrated WinHttp NEW, is not substituted into any NEW acceptance leg, and is not a claim of complete physical metadata validation.
Matrix reports retain `OldMetadataPhysicalLimitations`/`NewMetadataPhysicalLimitations`; `unmigrated-tooling-control.json` separately records the frozen/control hashes and audit findings.

The requested WinHttp corpus is checked into `HeaderAnnotationCompatibilityTests.WinHttpThreeWayMatrix`.
It covers request/access/authentication/scheme/URL flags, ref fields, raw and friendly entry points, callbacks, read buffers, interior-pointer header buffers, raw creation, and cleanup.
Canonical ownership tests separately execute contextual invalid values, output ownership, disposal while borrowed, and lease release, and run the lifetime analyzer.
The existing-native-enum compatibility checks also execute a converting unmanaged vtable call, including checked callers and native truncation, and compile both raw and enum scalar callers.

Both actual NEW WinHttp legs have now run; the four member-name source breaks above are observed, not predictions.
The harness records exact diagnostics at the unchanged `EnumFields` call sites and checks that changing only those member names restores compilation.
Other NEW diagnostics or public/generated-text deltas fail the checks rather than being waived.
Canonical ownership evidence remains separate: the WinHttp images provide no in-scope RAIIFree/invalid-handle facts and establish no WinHttp-specific automatic ownership or runtime SafeHandle proof.

All eight legal enum storage types are additionally checked on x86, x64, and ARM64 with deliberately different logical metadata storage.
Source-generated scalar/indirect tests verify direct enum signatures without conversion-marshaler attributes; they do not run the platform LibraryImport generator or native marshaling.
This checkpoint therefore does not establish runtime acceptance of LibraryImport generation.

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
Optionally add `-ToolingControlMetadata 'C:\snapshots\unmigrated-control\Windows.Win32.winmd' -ToolingControlSHA256 '<CONTROL SHA256>'` to audit the corrected-tooling control separately.
The control does not populate `NewMetadata` or change the frozen OLD matrix.
Reports and generated sources are written to `obj\HeaderAnnotationCompatibility`.
JSON records the exact baseline/proposed commits, built generator assembly hashes, metadata hashes, options, requested APIs, unchanged callers, public symbols, and diagnostics.
The original clean `41ffdb9` actual-image reports and generated sources are preserved locally under `obj\HeaderAnnotationCompatibility\checkpoint-41ffdb9-actual-new`.
Each `winhttp-{marshaling,no-marshaling}-scheme-member-adjustment.json` separately records the explicit four replacements, adjusted caller source and diagnostics; it does not overwrite an unchanged-caller grade.
`compatibility.trx` includes the canonical association-removal and pointer-depth mutation controls.
A dirty proposed checkout is explicitly marked `+working-tree`.
