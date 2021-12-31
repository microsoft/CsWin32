// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Windows.CsWin32;
using Xunit;

public class GeneratorOptionsTests
{
    [Fact]
    public void Validate_Default()
    {
        new GeneratorOptions().Validate();
    }
}
