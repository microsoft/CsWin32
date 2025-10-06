// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// An exception thrown when code generation fails because the requested type or member is not available given the target CPU architecture.
/// </summary>
[Serializable]
public class PlatformIncompatibleException : GenerationFailedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformIncompatibleException"/> class.
    /// </summary>
    public PlatformIncompatibleException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformIncompatibleException"/> class.
    /// </summary>
    /// <inheritdoc cref="Exception(string)" />
    public PlatformIncompatibleException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformIncompatibleException"/> class.
    /// </summary>
    /// <inheritdoc cref="Exception(string, Exception)" />
    public PlatformIncompatibleException(string message, Exception inner)
        : base(message, inner)
    {
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformIncompatibleException"/> class.
    /// </summary>
    /// <inheritdoc cref="Exception(System.Runtime.Serialization.SerializationInfo, System.Runtime.Serialization.StreamingContext)" />
    protected PlatformIncompatibleException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
    }
#endif
}
