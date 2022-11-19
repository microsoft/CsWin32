// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

internal class NumberedLineWriter : TextWriter
{
    private readonly ITestOutputHelper logger;
    private readonly StringBuilder lineBuilder = new StringBuilder();
    private int lineNumber;

    internal NumberedLineWriter(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    public override Encoding Encoding => Encoding.Unicode;

    public override void WriteLine(string? value)
    {
        this.logger.WriteLine($"{++this.lineNumber,6}: {this.lineBuilder}{value}");
        this.lineBuilder.Clear();
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            this.WriteLine(value.Substring(0, value.Length - 2));
        }
        else if (value.EndsWith("\n", StringComparison.Ordinal))
        {
            this.WriteLine(value.Substring(0, value.Length - 1));
        }
        else
        {
            this.lineBuilder.Append(value);
        }
    }
}
