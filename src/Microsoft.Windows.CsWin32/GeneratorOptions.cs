// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// Describes the options that feed into code generation.
/// </summary>
public record GeneratorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether ANSI functions will be omitted and the `W` suffix removed from from UTF-16 functions.
    /// </summary>
    public bool WideCharOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of a single class under which all p/invoke methods and constants are generated, regardless of imported module.
    /// </summary>
    /// <value>The default value is "PInvoke".</value>
    public string ClassName { get; set; } = "PInvoke";

    /// <summary>
    /// Gets or sets a value indicating whether to emit a single source file as opposed to types spread across many files.
    /// </summary>
    /// <value>The default value is <see langword="false" />.</value>
    public bool EmitSingleFile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to expose the generated APIs publicly (as opposed to internally).
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// Gets or sets options related to COM interop.
    /// </summary>
    public ComInteropOptions ComInterop { get; set; } = new ComInteropOptions();

    /// <summary>
    /// Gets or sets a value indicating whether to emit COM interfaces instead of structs, and allow generation of non-blittable structs for the sake of an easier to use API.
    /// </summary>
    /// <value>The default value is <see langword="true"/>.</value>
    public bool AllowMarshaling { get; set; } = true;

    /// <summary>
    /// Gets options related to friendly overloads.
    /// </summary>
    public FriendlyOverloadOptions FriendlyOverloads { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to generate APIs judged to be unnecessary or redundant given the target framework
    /// because the project multi-targets to frameworks that need the APIs consistently for easier coding.
    /// </summary>
    public bool MultiTargetingFriendlyAPIs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether friendly overloads should use safe handles.
    /// </summary>
    /// <value>The default value is <see langword="true"/>.</value>
    public bool UseSafeHandles { get; set; } = true;

    /// <summary>
    /// Throws an exception when this instance is not initialized with a valid set of values.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when some setting is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(this.ClassName))
        {
            throw new InvalidOperationException("The ClassName property must not be null or empty.");
        }
    }

    /// <summary>
    /// Options for COM interop.
    /// </summary>
    public record ComInteropOptions
    {
        /// <summary>
        /// Gets or sets an array of "interface.method" or "interface" strings that identify methods and interfaces that should be generated with <see cref="PreserveSigAttribute"/>.
        /// </summary>
        public ImmutableArray<string> PreserveSigMethods { get; set; } = ImmutableArray.Create<string>();

        /// <summary>
        /// Gets or sets a value indicating whether to emit methods that return COM objects via output parameters using <see cref="IntPtr"/> as the parameter type instead of the COM interface.
        /// </summary>
        /// <remarks>
        /// This may be useful on .NET when using ComWrappers. See <see href="https://github.com/microsoft/CsWin32/issues/328">this issue</see> for more details.
        /// </remarks>
        public bool UseIntPtrForComOutPointers { get; set; }
    }

    /// <summary>
    /// Options for friendly overloads.
    /// </summary>
    public record FriendlyOverloadOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to generate method overloads that may be easier to consume or be more idiomatic C#.
        /// </summary>
        /// <value>The default value is <see langword="true" />.</value>
        public bool Enabled { get; set; } = true;
    }
}
