// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Windows.CsWin32;
using Xunit;

public class GeneratorOptionsTests
{
    [Fact]
    public void Validate_Default()
    {
        new GeneratorOptions().Validate();
    }

    [Fact]
    public void Validate_EmptyNamespace()
    {
        Assert.Throws<InvalidOperationException>(() => new GeneratorOptions { Namespace = string.Empty }.Validate());
    }
}
