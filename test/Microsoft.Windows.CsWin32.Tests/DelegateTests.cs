// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class DelegateTests : GeneratorTestBase
{
    public DelegateTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory]
    [CombinatorialData]
    public void InterestingDelegates(
        [CombinatorialValues(
        "LPD3DHAL_RENDERSTATECB")] // A delegate with a pointer parameter to a managed struct.
        string name,
        bool allowMarshaling)
    {
        var options = DefaultTestGeneratorOptions with
        {
            AllowMarshaling = allowMarshaling,
        };
        this.GenerateApi(name);
    }
}
