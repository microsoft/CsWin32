// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal struct Rental<T> : IDisposable
    where T : class
{
    private Action<T, object?>? disposeAction;
    private T? value;
    private object? state;

    internal Rental(T value, Action<T, object?> disposeAction, object? state)
    {
        this.value = value;
        this.disposeAction = disposeAction;
        this.state = state;
    }

    public T Value => this.value ?? throw new ObjectDisposedException(this.GetType().FullName);

    public void Dispose()
    {
        T? value = this.value;
        this.value = null;
        if (value is not null)
        {
            this.disposeAction?.Invoke(value, this.state);
        }

        this.disposeAction = null;
        this.state = null;
    }
}
