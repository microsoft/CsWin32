// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;

namespace CsWin32Generator.Tests;

public partial class CsWin32GeneratorFullTests : CsWin32GeneratorTestsBase
{
    public CsWin32GeneratorFullTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    [Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
    public async Task FullGeneration()
    {
        this.fullGeneration = true;
        await this.InvokeGeneratorAndCompile(TestOptions.None, "FullGeneration");
    }
}
