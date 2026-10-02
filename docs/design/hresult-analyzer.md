**Analyzer goal:** prevent callers from ignoring failures from CsWin32-generated methods that are marked PreserveSig and return `HRESULT`.
# Scope

Analyze only CsWin32-generated methods that meet both conditions:

1. The method is marked PreserveSig.
2. The method returns the CsWin32 `HRESULT` type.
# Sufficient handling

1. Throw on failure: `obj.DoWork().ThrowOnFailure();`
2. Test success: `if (obj.DoWork().Succeeded) { ... }`
	1. Require `else` block
3. Test failure: `if (obj.DoWork().Failed) return;`
4. Compare with an HRESULT: `if (hr == HRESULT.S_FALSE) { ... }`
5. Switch on meaningful outcomes: `switch (hr) { case HRESULT.S_OK: ... }`
6. Pass to an approved consumer: `HandleHResult(hr);`
	1. **OPEN QUESTION**: Do we want to allow this? Or require handling in the immediate scope of the invocation
7. Suppress intentionally: `#pragma warning disable COMHR001`
	1. **OPEN QUESTION**: Do we want to prohibit this at the static analysis level? Or allow it and ensure authors are sufficiently grilled in code review for including it?

## Banned handling

1. Test the numeric value: `if (hr.Value < 0) return;`
	1. Prefer `hr.Failed`
2. `Marshal.ThrowExceptionForHR(hr.Value)`
	1. Prefer `hr.ThrowOnFailure()`

Handling may occur through a local variable:

```csharp
HRESULT hr = obj.DoWork();

if (hr.Failed)
{
    return;
}
```
## Switch requirements

   A switch is sufficient only if it distinguishes at least one meaningful HRESULT outcome and has a default arm. For example:

```csharp
switch (hr)
{
    case HRESULT.S_OK:
        UseResult();
        break;

    case HRESULT.S_FALSE:
        HandleNoResult();
        break;

    default:
        hr.ThrowOnFailure();
        break;
}
```

These are insufficient:

```csharp
switch (hr)
{
    default:
        break;
}
```

```csharp
switch (hr)
{
    case var result:
        Log(result);
        break;
}
```

The analyzer should require at least one non-catch-all case or guard that tests a specific HRESULT or success/failure category. A `default`, discard, or unguarded variable pattern does not count as inspection.
# Control-flow requirement

 Track each HRESULT through local variables. Every reachable path must perform sufficient handling before the value is lost.

 Report a diagnostic if an unchecked HRESULT:

1. Is discarded.
2. Is overwritten.
3. Reaches a normal method exit.
4. Is handled on only some reachable paths.
5. Escapes into a field, collection, delegate, or unknown method.

This is insufficient because the false path does not handle the HRESULT:

```csharp
HRESULT hr = obj.DoWork();

if (condition)
{
    hr.ThrowOnFailure();
}
```
# Insufficient handling

```csharp
obj.DoWork();
_ = obj.DoWork();
HRESULT hr = obj.DoWork();        // Never checked
Log(obj.DoWork());
Console.WriteLine(obj.DoWork());
int value = obj.DoWork().Value;
this.lastResult = obj.DoWork();
results.Add(obj.DoWork());
callback(obj.DoWork());
```

Merely reading, logging, storing, or passing the value to unknown code does not prove that failure was handled.
# Propagation through wrappers: open question

Consider a wrapper around a CsWin32 method:

```csharp
HRESULT Initialize()
{
    return native.Initialize();
}
```

Returning the HRESULT transfers responsibility. It does not handle the result.

If the analyzer checks only direct calls to CsWin32-generated methods, it will not enforce handling at this call site:

```csharp
Initialize();
```

The design must choose between two policies:

| Policy | Behavior | Tradeoff |
|---|---|---|
| Immediate-scope handling | Each CsWin32 invocation must be handled in the method containing the invocation. Returning the HRESULT is insufficient. | Simple and enforceable, but prevents transparent HRESULT wrappers. |
| Wrapper propagation | A wrapper can return the HRESULT, and calls to qualifying wrappers are also analyzed. | Supports abstraction, but requires a reliable way to identify wrappers and expands the analyzer beyond direct CsWin32 calls. |
# Diagnostics

| Rule       | Condition                                               |
| ---------- | ------------------------------------------------------- |
| `COMHR001` | HRESULT is directly discarded                           |
| `COMHR002` | HRESULT is stored locally but not handled on every path |

   `COMHR001` is simple and should have few false positives. `COMHR002` requires control-flow analysis.
# Analysis boundary

Use local control-flow analysis to track HRESULT values through local variables.

Do not try to follow values through:

1. Fields.
2. Collections.
3. Delegates.
4. Reflection.
5. Arbitrary external methods.

Treat these cases as unchecked escapes unless the destination is an explicitly approved HRESULT consumer.