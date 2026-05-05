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

    [Fact]
    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FullGen-Net8")]
    public async Task FullGeneration_Net8()
    {
        this.fullGeneration = true;
        this.compilation = this.starterCompilations["net8.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp12);
        this.nativeMethodsJson = "NativeMethods.EmitSingleFile.json";
        await this.InvokeGeneratorAndCompile("FullGeneration_net8.0_CSharp12", TestOptions.None);
    }

    [Fact]
    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FullGen-Net9")]
    public async Task FullGeneration_Net9()
    {
        this.fullGeneration = true;
        this.compilation = this.starterCompilations["net9.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp13);
        this.nativeMethodsJson = "NativeMethods.EmitSingleFile.json";
        await this.InvokeGeneratorAndCompile("FullGeneration_net9.0_CSharp13", TestOptions.None);
    }

    [Fact]
    [Trait("TestCategory", "HighMemory")]
    [Trait("TestShard", "FullGen-Net9-Ptrs")]
    public async Task FullGeneration_Net9_Pointers()
    {
        this.fullGeneration = true;
        this.compilation = this.starterCompilations["net9.0"];
        this.parseOptions = this.parseOptions.WithLanguageVersion(LanguageVersion.CSharp13);
        this.nativeMethodsJson = "NativeMethods.IncludePointerOverloads.json";
        await this.InvokeGeneratorAndCompile("FullGeneration_net9.0_CSharp13_pointers", TestOptions.None);
    }
}
