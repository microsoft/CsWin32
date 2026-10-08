// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Describes generated resource access for the CsWin32 lifetime analyzer.
/// </summary>
[global::System.AttributeUsage(global::System.AttributeTargets.Property | global::System.AttributeTargets.Struct)]
internal sealed class NativeOwnershipAttribute : global::System.Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeOwnershipAttribute"/> class.
    /// </summary>
    /// <param name="releaseMethod">The native cleanup function.</param>
    /// <param name="isLease">Whether the annotated type represents a scoped reference.</param>
    public NativeOwnershipAttribute(string releaseMethod, bool isLease = false)
    {
    }
}
