# Features

- Generates interop code quickly at compilation time.
- Generates friendly overloads/extensions (including `SafeHandle`-types support).
- Generates xml documentation based on and links back to learn.microsoft.com
- Ships no bulky assemblies alongside your application.
- [Layered composition](composition.md): multiple assemblies can extend a single shared `PInvoke` static class so callers reach every native API through one symbol.

![Animation demonstrating p/invoke code generation](../images/demo.gif)

## Buffers containing interior pointers

Some native APIs write records whose pointer fields refer to other data inside the
caller's output buffer. When metadata marks a buffer parameter with
`ContainsInteriorPointersAttribute`, CsWin32 keeps that parameter as a native pointer
instead of generating a span, managed array, or `in`/`ref`/`out` buffer projection.
The buffer's capacity parameter remains explicit even when it is optional, and other
buffers sharing that capacity also keep their pointer types. Unrelated parameters
can still receive friendly projections, including omission of optional outputs.

The caller must keep the buffer alive and at the same address throughout both the
native call and consumption of the returned pointers. Use unmanaged storage, stack
storage, or an outer `fixed` scope that covers the call and all decoding or copying.
A span overload's temporary pin would cover only the native call; `GC.KeepAlive`
alone does not prevent relocation. Copying a pointer-bearing record or buffer does
not rebase its pointers or make the pointed-to data independently owned.

This behavior depends on the annotation being present in the selected metadata.
Pointer fields alone do not establish that they borrow the containing buffer.
Offset-based self-relative data, such as a self-relative security descriptor, does
not have the same relocation restriction until raw pointers into it are extracted.
