// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;

    /// <summary>
    /// Describes the options that feed into code generation.
    /// </summary>
    public record GeneratorOptions
    {
        /// <summary>
        /// Gets a value indicating whether ANSI functions will be omitted and the `W` suffix removed from from UTF-16 functions.
        /// </summary>
        public bool WideCharOnly { get; init; } = true;

        /// <summary>
        /// Gets the name of a single class under which all p/invoke methods should be added, regardless of imported module. Use null for one class per imported module.
        /// </summary>
        /// <value>The default value is "PInvoke".</value>
        public string? ClassName { get; init; } = "PInvoke";

        /// <summary>
        /// Gets the namespace for generated code.
        /// </summary>
        /// <value>The default value is "Microsoft.Windows.Sdk". Must be non-empty.</value>
        public string Namespace { get; init; } = "Microsoft.Windows.Sdk";

        /// <summary>
        /// Gets a value indicating whether to emit a single source file as opposed to types spread across many files.
        /// </summary>
        /// <value>The default value is <see langword="false" />.</value>
        public bool EmitSingleFile { get; init; }

        /// <summary>
        /// Gets a value indicating whether to expose the generated APIs publicly (as opposed to internally).
        /// </summary>
        public bool Public { get; init; }

        /// <summary>
        /// Throws an exception when this instance is not initialized with a valid set of values.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when some setting is invalid.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.Namespace))
            {
                throw new InvalidOperationException("The namespace must be set.");
            }
        }
    }
}
