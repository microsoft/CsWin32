# Automated generation of C# bindings for the Win32 API

Get immediate access to the full Win32 API from C#, just by naming the APIs you require.
Generated APIs will be accurate for your CPU architecture and target Windows version.
All APIs are generated directly into your project, allowing you to ship without any additional runtime dependencies.

Reach functionality that .NET doesn't expose via the Base Class Library (BCL) to give your library or application that feature that really sets it apart.

Install this package, then create a NativeMethods.txt file with a list of any APIs you need for example:

NativeMethods.txt:

```
CreateFile
IUIRibbon
S_OK
NTSTATUS
IsPwrHibernateAllowed
ISpellChecker
``````

Any supporting APIs (e.g. enums, structs) are automatically generated when they are required by what you've directly asked for.

Call extern methods through the `PInvoke` class

```cs
using SafeHandle f = PInvoke.CreateFile(
    "some.txt",
    (uint)GENERIC_ACCESS_RIGHTS.GENERIC_READ,
    FILE_SHARE_MODE.FILE_SHARE_READ,
    lpSecurityAttributes: null,
    FILE_CREATION_DISPOSITION.CREATE_ALWAYS,
    FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
    hTemplateFile: null);
```

Learn more from [our README on GitHub](https://github.com/microsoft/CsWin32#readme).
