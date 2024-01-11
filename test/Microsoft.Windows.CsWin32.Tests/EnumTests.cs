// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class EnumTests : GeneratorTestBase
{
    public EnumTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void EnumsIncludeAssociatedConstants()
    {
        this.GenerateApi("SERVICE_ERROR");
        EnumDeclarationSyntax enumDecl = Assert.IsType<EnumDeclarationSyntax>(this.FindGeneratedType("SERVICE_ERROR").Single());

        // The enum should contain the constant.
        Assert.Contains(enumDecl.Members, value => value.Identifier.ValueText == "SERVICE_NO_CHANGE");

        // The constant should not be generated as a separate constant.
        Assert.Empty(this.FindGeneratedConstant("SERVICE_NO_CHANGE"));
    }
}
