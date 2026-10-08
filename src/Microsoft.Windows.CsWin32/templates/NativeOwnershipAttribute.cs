// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Describes generated resource access for the CsWin32 lifetime analyzer.
/// </summary>
/// <remarks>
/// A valid lifetime scope does not make manually freeing the resource safe. A lease
/// prevents its SafeHandle from releasing the resource, but cannot prevent a native
/// cleanup call from freeing it directly. The analyzer uses the cleanup function's
/// identity to reject such calls even within a valid owner or lease scope.
/// This attribute provides analysis metadata; it does not acquire references or perform cleanup.
/// </remarks>
[global::System.AttributeUsage(global::System.AttributeTargets.Property | global::System.AttributeTargets.Struct)]
internal sealed class NativeOwnershipAttribute : global::System.Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeOwnershipAttribute"/> class.
    /// </summary>
    /// <param name="releaseMethod">The native cleanup function callers must not invoke with the owned value.</param>
    /// <param name="isLease">Whether the annotated type represents a scoped reference.</param>
    public NativeOwnershipAttribute(string releaseMethod, bool isLease = false)
    {
    }
}
