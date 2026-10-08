// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Keeps a reference to the original resource until this lease's using scope ends.
/// </summary>
/// <remarks>
/// Create this lease with <c>using var lease = owner.Lease()</c>. Do not copy it,
/// store it in a field, pass it to another method, return it, or dispose it explicitly.
/// </remarks>
public ref struct LeaseScope
{
#nullable enable
    private __OwnerType? owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeaseScope"/> struct.
    /// </summary>
    /// <param name="owner">The owner whose reference count is acquired.</param>
    internal LeaseScope(__OwnerType owner)
    {
        this.owner = null;
        bool added = false;
        try
        {
            owner.DangerousAddRef(ref added);
            this.owner = owner;
        }
        catch
        {
            if (added)
            {
                owner.DangerousRelease();
            }

            throw;
        }
    }

    /// <summary>
    /// Gets the native resource while this lease is active.
    /// </summary>
    /// <exception cref="global::System.ObjectDisposedException">The lease is not active.</exception>
    public readonly __NativeType Value => this.owner is { } owner
        ? owner.DangerousValue
        : throw new global::System.ObjectDisposedException(nameof(LeaseScope));

    /// <summary>
    /// Releases this lease's reference without disposing the original owner.
    /// </summary>
    public void Dispose()
    {
        __OwnerType? owner = this.owner;
        this.owner = null;
        if (owner is not null)
        {
            owner.DangerousRelease();
        }
    }
}
