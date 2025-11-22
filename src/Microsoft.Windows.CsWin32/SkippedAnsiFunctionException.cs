// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETSTANDARD2_0
using System.Runtime.Serialization;
#endif

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// An exception thrown when code generation skips an explicitly requested ANSI-only function because <c>WideCharOnly</c> is set to <see langword="true"/>.
/// </summary>
[Serializable]
public class SkippedAnsiFunctionException : GenerationFailedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedAnsiFunctionException"/> class.
    /// </summary>
    public SkippedAnsiFunctionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedAnsiFunctionException"/> class with the specified function name.
    /// </summary>
    /// <param name="functionName">The ANSI function name that was skipped.</param>
    public SkippedAnsiFunctionException(string functionName)
        : base($"The ANSI function \"{functionName}\" will not be generated because WideCharOnly is set to true")
    {
        this.FunctionName = functionName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedAnsiFunctionException"/> class with a custom message and inner exception.
    /// </summary>
    /// <param name="functionName">The ANSI function name that was skipped.</param>
    /// <param name="inner">The inner exception.</param>
    public SkippedAnsiFunctionException(string functionName, Exception inner)
        : base($"The ANSI function \"{functionName}\" will not be generated because WideCharOnly is set to true", inner)
    {
        this.FunctionName = functionName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedAnsiFunctionException"/> class during deserialization.
    /// </summary>
    /// <param name="info">The serialization information.</param>
    /// <param name="context">The streaming context.</param>
    protected SkippedAnsiFunctionException(
      SerializationInfo info,
      StreamingContext context)
        : base(info, context)
    {
        this.FunctionName = info.GetString(nameof(this.FunctionName)) ?? string.Empty;
    }

    /// <summary>
    /// Gets the ANSI function name that was skipped.
    /// </summary>
    public string? FunctionName { get; }

    /// <inheritdoc/>
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(this.FunctionName), this.FunctionName);
    }
}
