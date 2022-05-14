// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

internal static class TestUtils
{
    private const string ExpectedGeneratedSourceLineEnding = "\r\n";
    private static readonly Regex NewLineRegex = new(@"\r?\n");

    public static string NormalizeToExpectedLineEndings(string text)
    {
        return NewLineRegex.Replace(text, ExpectedGeneratedSourceLineEnding);
    }
}
