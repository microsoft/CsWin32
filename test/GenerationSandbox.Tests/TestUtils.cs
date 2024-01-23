// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

internal static class TestUtils
{
#if DEBUG // Only tests that are conditioned for Debug mode can assert this.
    internal static void AssertDebugAssertFailed(Action action)
    {
        // We're mutating a static collection.
        // Protect against concurrent tests mutating the collection while we're using it.
        lock (Trace.Listeners)
        {
            TraceListener[] listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new ThrowingTraceListener());

            try
            {
                action();
                Assert.Fail("Expected Debug.Assert to fail.");
            }
            catch (DebugAssertFailedException)
            {
                // PASS
            }
            finally
            {
                Trace.Listeners.Clear();
                Trace.Listeners.AddRange(listeners);
            }
        }
    }
#endif

    private class DebugAssertFailedException : Exception
    {
    }

    private class ThrowingTraceListener : TraceListener
    {
        public override void Fail(string? message) => throw new DebugAssertFailedException();

        public override void Fail(string? message, string? detailMessage) => throw new DebugAssertFailedException();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }
    }
}
