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
    /// Gets or sets the simple (unqualified) name of a CsWin32-generated static class in the same namespace whose members the generated APIs should appear as C# 14 extension members of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, the generated <see cref="ClassName"/> class wraps its p/invoke methods, friendly overloads, macros and helper methods in an <c>extension (Receiver) { ... }</c> block so callers may discover them through the receiver type. Constants remain on the host class as <see langword="private"/> fields and are surfaced through generated <see langword="static"/> properties inside the extension block.
    /// </para>
    /// <para>
    /// The receiver type must be another CsWin32-generated static class in the same namespace, visible to the consuming compilation (either <see langword="public"/> or via <c>[InternalsVisibleTo]</c>). Requires C# 14 (<c>LangVersion</c> 14 or later) and the Roslyn 5 leg of the analyzer; otherwise a diagnostic is reported.
    /// </para>
    /// </remarks>
    /// <value>The default value is <see langword="null"/>, which means no extension block is generated.</value>
    public string? ExtensionReceiver { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to emit a single source file as opposed to types spread across many files, 'null' indicates to use the recommended default for the environment.
    /// </summary>
    /// <value>The default value is <see langword="null" />.</value>
    public bool? EmitSingleFile { get; set; }

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
    /// Gets or sets options related to friendly overloads.
    /// </summary>
    public FriendlyOverloadOptions FriendlyOverloads { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to generate APIs judged to be unnecessary or redundant given the target framework
    /// because the project multi-targets to frameworks that need the APIs consistently for easier coding.
    /// </summary>
    public bool MultiTargetingFriendlyAPIs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether fields nested within anonymous structs and unions are surfaced as ref-returning properties on the declaring struct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows metadata models anonymous nested structs and unions as named nested types (e.g. <c>_Anonymous_e__Union</c>) reached through a generated holder field named <c>Anonymous</c> (or <c>Anonymous1</c>, <c>Anonymous2</c>, etc.). This forces awkward access such as <c>value.Anonymous.Anonymous.field</c>. When this option is enabled, a <c>[UnscopedRef] ref</c> property is generated on the declaring struct for each such nested field so the field may be read, written, and pointed to directly as <c>value.field</c>.
    /// </para>
    /// <para>
    /// Only fields reached <em>exclusively</em> through anonymous holders are flattened. A <em>named</em> nested struct or union (e.g. <c>KEY_EVENT_RECORD.uChar</c>) is left alone, and a nested struct <em>value</em> reached through an anonymous holder is surfaced as a single <c>ref</c> to the whole value rather than having its own members hoisted; flattening never digs through a nested struct value into its fields.
    /// </para>
    /// <para>
    /// The generated accessors require C# 11 or later (for <see cref="System.Diagnostics.CodeAnalysis.UnscopedRefAttribute"/>); when an older language version is in use, no accessors are generated and the <c>Anonymous</c> holder remains the only access path. The original <c>Anonymous</c> holder fields are always retained, so the flattened accessors are purely additive.
    /// </para>
    /// </remarks>
    /// <value>The default value is <see langword="true"/>.</value>
    public bool FlattenNestedAnonymousTypes { get; set; } = true;

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

        if (this.ExtensionReceiver is { } receiver && string.IsNullOrWhiteSpace(receiver))
        {
            throw new InvalidOperationException("The ExtensionReceiver property must not be empty or whitespace when set.");
        }

        // Note: ExtensionReceiver == ClassName (self-reference) is detected at generation time and surfaces as the
        // PInvoke011 diagnostic, allowing the rest of generation to continue without the extension wrap.
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

        /// <summary>
        /// Gets or sets a value indicating whether to emit code that relies on [GeneratedComInterface] and [GeneratedComClass].
        /// Only takes effect if MSBuild property '&lt;CsWin32RunAsBuildTask&gt;true&lt;/CsWin32RunAsBuildTask&gt;' is set.
        /// </summary>
        /// <value>The default value is <see langword="null"/>.</value>
        public bool? UseComSourceGenerators { get; set; }
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

        /// <summary>
        /// Gets or sets a value indicating whether to also generate overloads that use pointer types for parameters that are [MemorySize] annotated buffers
        /// which normally appear as spans.
        /// </summary>
        public bool IncludePointerOverloads { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to generate generic <c>&lt;T&gt;</c> overloads for methods
        /// with the IID_PPV_ARGS pattern (a <c>Guid*</c> parameter immediately preceding a <c>void**</c> <c>[ComOutPtr]</c> parameter),
        /// where the GUID is derived from <c>typeof(T).GUID</c> and the output pointer is typed as <c>T</c>.
        /// </summary>
        /// <value>The default value is <see langword="true"/>.</value>
        public bool ComOutPtrGenericOverloads { get; set; } = true;
    }
}
