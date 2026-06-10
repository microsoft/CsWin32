# Layered composition with `extensionReceiver`

CsWin32 lets multiple assemblies contribute to a single, unified static class for native API
discovery. One assembly hosts the canonical `PInvoke` symbol; every other assembly that uses
CsWin32 extends it. Callers reach every native API through `PInvoke.X()` regardless of which
assembly emitted it.

This is built on C# 14 [extension members][csharp14-extensions] and is configured via the
`extensionReceiver` setting in `NativeMethods.json`.

## When to use it

Use `extensionReceiver` if your codebase is layered — a low-level library, a UI/runtime helper
library on top, and an application on top of that — and you want callers to use a single
`PInvoke.X()` discovery surface regardless of which layer happens to declare `X`.

Don't use it for single-assembly projects. The default `partial class PInvoke` already gives you
everything you need without C# 14.

## Requirements

C# 14 or later in every project that consumes the generated extension members. This is the
default when targeting .NET 10 or later; otherwise set `<LangVersion>14</LangVersion>` (or
`Preview` / `Latest` / `LatestMajor`) in the project file.

## Two roles

Every assembly that runs CsWin32 plays exactly one role:

| Role | `NativeMethods.json` shape | What the generator emits |
|---|---|---|
| **Owner** | `extensionReceiver` *not* set | A plain `partial class <className>` with members declared directly on it. |
| **Extender** | `extensionReceiver: "<OwnerClassName>"` set | A `partial class <className>` whose members live inside an `extension(<OwnerClassName>) { … }` block. |

In a layered stack, the lowest assembly is the owner; every higher assembly is an extender. Each
extender must use a `className` that differs from the owner's and from every other extender's.

## A worked example

Three projects in dependency order: `MyApp.Core` (lowest), `MyApp.Helpers`, `MyApp.App` (highest).

### Owner — `MyApp.Core`

```jsonc
// NativeMethods.json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "className": "PInvoke",
  "public": true
}
```

```text
// NativeMethods.txt
GetTickCount
```

Generates:

```csharp
namespace Windows.Win32
{
    public static partial class PInvoke
    {
        [DllImport("KERNEL32.dll", ExactSpelling = true)]
        public static extern uint GetTickCount();
    }
}
```

### Extender — `MyApp.Helpers` (references `MyApp.Core`)

```jsonc
// NativeMethods.json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "className": "PInvokeHelpers",
  "extensionReceiver": "PInvoke",
  "public": true
}
```

```text
// NativeMethods.txt
GetForegroundWindow
```

Generates:

```csharp
namespace Windows.Win32
{
    public static partial class PInvokeHelpers
    {
        extension (global::Windows.Win32.PInvoke)
        {
            [DllImport("USER32.dll", ExactSpelling = true)]
            public static extern global::Windows.Win32.Foundation.HWND GetForegroundWindow();
        }
    }
}
```

### Extender — `MyApp.App` (references both)

```jsonc
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "className": "PInvokeApp",
  "extensionReceiver": "PInvoke"
}
```

```text
IsWindow
```

### Consuming code

Application code reaches every API through the single `PInvoke` symbol:

```csharp
using Windows.Win32;

uint   ticks = PInvoke.GetTickCount();        // declared in MyApp.Core
HWND   hwnd  = PInvoke.GetForegroundWindow(); // declared in MyApp.Helpers
bool   alive = PInvoke.IsWindow(hwnd);        // declared in MyApp.App
```

The `using Windows.Win32;` directive at the top of the file is what makes the extension members
discoverable. See [Dispatch rules](#dispatch-rules) below.

## Dispatch rules

C# 14 extension members participate in member lookup only when the static class that declares the
`extension(T) { … }` block is in scope at the call site. Three practical rules cover almost every
case.

### Rule 1 — Bring the extender's namespace into scope

To call `PInvoke.X()` and have the compiler find `X` in an extender, the call site must either be
inside `namespace Windows.Win32 { … }` or contain a **file-scoped** `using Windows.Win32;`.

> [!IMPORTANT]
> `global using Windows.Win32;` in a `GlobalUsings.cs` file works for most lookups, but it does
> **not** contribute to type-name resolution inside user-authored `extension(T)` blocks. If you
> author your own extension blocks, add the file-scoped `using` in those files even if the same
> namespace is declared as a global using.

### Rule 2 — When you can't use the extension path, use the host class directly

Some C# contexts cannot resolve through an extension property. Specifically:

- `enum` initializers
- attribute arguments
- `fixed`-array sizes
- `case` labels of `switch`

For these contexts, reach the value through the host class:

```csharp
// Works in any context, including const contexts.
const uint Sentinel = PInvoke.WM_NULL;       // host class on the owner
enum MyFlags { Default = (int)PInvokeHelpers.SomeFlag }  // host class on an extender
```

(See [Constants](#constants) below for why this is necessary.)

### Rule 3 — Inside an extension block, qualify every call

When you author your own `extension(T) { … }` block, calls to sibling extension members are *not*
discovered through unqualified names. Always write `T.X(…)`:

```csharp
using Windows.Win32;

namespace Windows.Win32;

internal static class MyExtraHelpers
{
    extension (PInvoke)
    {
        public static int RunWithCleanup()
        {
            // ✅ Qualify with the receiver. Anything else is wrong.
            uint t = PInvoke.GetTickCount();
            return (int)t;
        }
    }
}
```

Forgetting the qualifier produces `CS0103` in the simple case — or, if the surrounding method
happens to have the same name as the API it wraps, *silent infinite recursion at runtime*. Qualify
always.

## Constants

C# 14 extension blocks cannot contain `const` fields or `static readonly` fields. CsWin32 therefore
emits a constant in two forms:

```csharp
public static partial class PInvoke
{
    // 1. The real const lives on the host class. Reachable as `PInvoke.WM_NULL` in any context,
    //    including enum initializers, attribute arguments, `fixed`-array sizes, and case labels.
    public const uint WM_NULL = 0u;

    extension (global::Windows.Win32.PInvoke)
    {
        // 2. A forwarder property surfaces the same value through the receiver type for runtime
        //    contexts. `PInvoke.WM_NULL` resolves to this when not in a const context.
        public static uint WM_NULL => global::Windows.Win32.PInvoke.WM_NULL;
    }
}
```

For an extender (e.g. `PInvokeHelpers`), the const lives on the extender's host class
(`PInvokeHelpers.X`) and the forwarder lives on the receiver (`PInvoke.X`). You can always reach
the const via the host class.

## How duplicates are handled across layers

When an extender's `NativeMethods.txt` requests something the owner (or any other referenced
extender) already exposes on the receiver, CsWin32 detects the duplicate and does not re-emit it.

The match is signature-based: same simple name, same parameter count, same parameter type names
(after normalization that strips `global::` and leading namespace segments). A user-authored
wrapper of the same name but a different signature is treated as a distinct overload and does *not*
cause the metadata extern to be skipped.

| Already on receiver | This layer wants | Result |
|---|---|---|
| `uint GetTickCount()` | `uint GetTickCount()` | Skipped — full signature match. |
| `string Lookup(string, string)` (user wrapper) | `int Lookup(HKEY, PCWSTR, …)` | Emitted — parameter counts/types differ. |
| `int WM_NULL` field or property | `uint WM_NULL` constant | Skipped — name + arity 0 match. |

For most multi-layer setups this is automatic. The cases where you need to think about it are
listed in the [migration section](#migration-from-a-multi-layer-project) below.

## Diagnostics

The generator emits these diagnostics for misconfigurations:

| Diagnostic | When | Resolution |
|---|---|---|
| `PInvoke011` | The configured `extensionReceiver` could not be resolved in any namespace served by your CsWin32 generators, or it resolves to something other than a static class, or it equals `className`. | Verify the receiver type exists, is `static class`, is accessible (`public` from another assembly or `[InternalsVisibleTo]`), and is not the same as `className`. |
| `PInvoke012` | The consuming project's `LangVersion` is less than C# 14. | Set `<LangVersion>14</LangVersion>` (or `Preview` / `Latest` / `LatestMajor`) in the project that uses the extension members. Targeting .NET 10 or later picks this up automatically. |
| `PInvoke013` | The host's analyzer doesn't support C# 14 extension members. | Upgrade to a newer SDK or Visual Studio, or remove the `extensionReceiver` setting. |

## Migration from a multi-layer project

The following steps mirror the work most existing multi-layer codebases need to go through to
adopt the feature. Each step is independently reversible if you change your mind partway through.

### Step 1 — Inventory

For every project that already runs CsWin32, record:

- Its `className` value (or `PInvoke` if unset).
- Its `public` flag.
- Whether it ships any hand-authored `partial` of a type that CsWin32 also emits (e.g. a
  `Foundation/HRESULT.cs`, a `PInvoke.SomeWrapper.cs`).

### Step 2 — Choose the owner

Pick the project that sits at the bottom of your dependency graph — the one referenced by every
other CsWin32-consuming project, that doesn't itself reference any of them. Usually this is the
"core" or "primitives" project.

If the owner's existing `className` is not `"PInvoke"`, rename it now. Every extender will reach
the owner through that name; making it the unsurprising default reduces friction for consumers.

### Step 3 — Make the receiver visible to extenders

The receiver type must be reachable from every extending project. The simplest pattern:

```jsonc
// Owner's NativeMethods.json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "className": "PInvoke",
  "public": true
}
```

Setting `public: true` on the owner makes the receiver visible to any consumer. If your owner
intends to keep all its API surface internal, you can instead apply
`[InternalsVisibleTo("<each extender>")]` from the owner — but be aware that final application code
won't be able to use `PInvoke.X()` to reach extender members unless it, too, can see the owner's
receiver type.

> [!NOTE]
> Setting `public: true` on the owner publishes **every** type CsWin32 emits in that assembly,
> including foundation types like `HRESULT`, `BSTR`, `PWSTR`, `BOOL`. If higher layers already
> ship their own `partial` of those types, you'll need to consolidate them (Step 6).

### Step 4 — Flip extenders

In every extending project's `NativeMethods.json`:

```jsonc
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "className": "PInvokeMyLibrary",
  "extensionReceiver": "PInvoke",
  "public": true
}
```

Rename `className` to something unique per assembly (a common convention is
`PInvoke<AssemblyShortName>`) and add `extensionReceiver: "<OwnerClassName>"`.

### Step 5 — Bump `LangVersion` to 14 in every consuming project

Targeting .NET 10 or later already implies C# 14. For other target frameworks, set
`<LangVersion>14</LangVersion>` (or `Preview`/`Latest`/`LatestMajor`) in `Directory.Build.props`
or in each `.csproj`. Make sure it applies to every TargetFramework you build, including `net472`
/ `net48` legs if you multi-target. The generated `extension(T) { … }` blocks won't parse with an
older language version.

### Step 6 — Consolidate hand-authored partials

If a higher layer ships its own `partial` of a type that the owner now emits publicly (commonly
`HRESULT`, `BSTR`, `PWSTR`, custom handle types), the higher layer's partial will collide with the
owner's now-imported type and the compiler will emit `CS0436`.

Apply the following rule, in order, to each conflicting partial:

1. If the partial's members already exist on the owner's CsWin32-generated version (typical for
   well-known helpers), **delete** the duplicate.
2. If the partial adds methods, properties, or indexers that the owner doesn't have, **move them
   into an `extension(<Type>) { … }` block** in a sibling static class within the higher layer.
3. If the partial adds constants, `static readonly` fields, nested types, or operators (none of
   which can live in a C# 14 extension block), **add them to the owner's `NativeMethods.txt`** so
   the owner emits them, or, if that isn't possible, declare them in a non-conflicting sibling
   type in the higher layer.

The general principle:

> When there are partial-definition collisions between layers, move the additions to an extension
> on the type as defined lower in the stack — unless the equivalent functionality is already there.
> When something cannot be lifted into an extension, decide explicitly where it lives instead.

### Step 7 — Rewrite call sites

Sweep your code for `<OldClassName>.<member>` references in extenders and rewrite them to
`<OwnerClassName>.<member>` — for the typical owner-named-`PInvoke`, this is just `PInvoke.<member>`.

For every file that now reaches a native API:

- If the file is already inside `namespace Windows.Win32 { … }`, no change needed.
- Otherwise, add a file-scoped `using Windows.Win32;` at the top.

For any constant referenced in a C# const context (enum initializer, attribute argument,
`fixed`-array size, `case` label), keep the host-class-qualified form (`<HostClass>.X`) — the
extension forwarder is a property, not a const.

### Step 8 — Fix XML doc cref references

XML doc tooling cannot resolve `<see cref="PInvoke.X(…)"/>` when `X` is an extension member.
With `TreatWarningsAsErrors` on, this surfaces as `CS1574` and fails the build.

Rewrite affected crefs to inline code:

```diff
- /// <see cref="PInvoke.GetTickCount"/>
+ /// <c>PInvoke.GetTickCount</c>
```

Where the cref pointed at a direct member on the host class that still exists post-migration,
leave it alone.

### Step 9 — Build, test, ship

Build every TargetFramework. Tests should pass unchanged — the feature is a discovery-surface
change, not a runtime behavior change.

## Common issues

### `'PInvoke' does not contain a definition for 'X'` (`CS0117`)

The call site can't see the extender that declares `X`.

- Add `using Windows.Win32;` (file-scoped) in the file.
- Confirm the extender's host class (and its containing assembly) are reachable: `public`, or
  `internal` plus `[InternalsVisibleTo]`.
- If you author your own `extension(T) { … }` and rely on a `global using` to bring another
  namespace into scope, switch to a file-scoped `using`.

### Constant in a const context fails to compile (`CS0133`)

You wrote `PInvoke.X` in a context that requires a `const`. The extension path is a property and
properties are not constants. Use `<HostClass>.X` — the const lives there.

### Self-recursion in a user-authored wrapper

Inside an `extension(T)` block, unqualified calls do not find sibling extension members of the
same name. Always qualify as `T.X(…)`. Without the qualifier, the wrapper either fails to compile
(`CS0103`) or, if its own name matches, recurses into itself at runtime.

### Type conflicts with the owner after promoting it to `public` (`CS0436`)

A higher layer ships a partial of a type that the owner now exports. See [Step 6](#step-6--consolidate-hand-authored-partials).

### `extension` keyword unrecognized on a legacy TFM leg

The consuming project's `LangVersion` is < 14 on at least one TargetFramework. Set
`<LangVersion>14</LangVersion>` for every leg that isn't already on .NET 10+ (typically in
`Directory.Build.props`).

### Argument ambiguity on `null` (`CS1503` / `CS1620`)

CsWin32 adds `out T` friendly overloads alongside the raw `T*` parameter. Existing call sites that
pass `null` to the raw pointer may become ambiguous. Replace `null` with `out _` (use the
overload) or with a typed `(T*)null` cast (force the raw-pointer overload).

## What can and can't live in an `extension(T)` block

The C# 14 language defines what extension blocks can contain. Helpful to consult when deciding how
to migrate a hand-authored partial.

| Construct | Allowed in `extension(T)`? |
|---|---|
| Static methods | ✅ |
| Instance methods (on the receiver value) | ✅ |
| Static / instance properties | ✅ |
| Indexers | ✅ (some shapes) |
| Static constructors | ❌ |
| Fields (any) | ❌ |
| `const` | ❌ |
| Nested types | ❌ |
| Operators | Partial — only specific shapes |

For members that can't be expressed as extension members, the migration playbook in
[Step 6](#step-6--consolidate-hand-authored-partials) covers where they belong instead.

[csharp14-extensions]: https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14#extension-members
