// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsWin32Generator.Tests;

public partial class CsWin32GeneratorFullTests : CsWin32GeneratorTestsBase
{
    public CsWin32GeneratorFullTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory]
    [Trait("TestCategory", "FailsInCloudTest")] // these take ~4GB of memory to run.
    [InlineData("net8.0", LanguageVersion.CSharp12)]
    [InlineData("net9.0", LanguageVersion.CSharp13)]
    public async Task FullGeneration(string tfm, LanguageVersion langVersion)
    {
        this.fullGeneration = true;
        this.compilation = this.starterCompilations[tfm];
        this.parseOptions = this.parseOptions.WithLanguageVersion(langVersion);
        this.nativeMethodsJson = "NativeMethods.EmitSingleFile.json";
        await this.InvokeGeneratorAndCompile($"FullGeneration_{tfm}_{langVersion}", TestOptions.None);
    }
}
