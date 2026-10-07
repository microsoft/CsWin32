// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Tests projections of native buffers using the published Win32 metadata package.
/// </summary>
public class InteriorPointerTests : GeneratorTestBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InteriorPointerTests"/> class.
    /// </summary>
    /// <param name="logger">The test output logger.</param>
    public InteriorPointerTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Gets the annotated buffer sites supplied by the published SDK metadata package.
    /// </summary>
    public static IEnumerable<object[]> PublishedMetadataBufferCases
    {
        get
        {
            (string Api, string Buffer, string Capacity)[] sites =
            [
                ("AddJobA", "pData", "cbBuf"),
                ("AddJobW", "pData", "cbBuf"),
                ("EnumFormsA", "pForm", "cbBuf"),
                ("EnumFormsW", "pForm", "cbBuf"),
                ("GetFormA", "pForm", "cbBuf"),
                ("GetFormW", "pForm", "cbBuf"),
                ("EnumJobsA", "pJob", "cbBuf"),
                ("EnumJobsW", "pJob", "cbBuf"),
                ("GetJobA", "pJob", "cbBuf"),
                ("GetJobW", "pJob", "cbBuf"),
                ("EnumMonitorsA", "pMonitor", "cbBuf"),
                ("EnumMonitorsW", "pMonitor", "cbBuf"),
                ("EnumPortsA", "pPort", "cbBuf"),
                ("EnumPortsW", "pPort", "cbBuf"),
                ("EnumPrinterDataExA", "pEnumValues", "cbEnumValues"),
                ("EnumPrinterDataExW", "pEnumValues", "cbEnumValues"),
                ("EnumPrinterDriversA", "pDriverInfo", "cbBuf"),
                ("EnumPrinterDriversW", "pDriverInfo", "cbBuf"),
                ("GetPrinterDriverA", "pDriverInfo", "cbBuf"),
                ("GetPrinterDriverW", "pDriverInfo", "cbBuf"),
                ("GetPrinterDriver2W", "pDriverInfo", "cbBuf"),
                ("EnumPrintersA", "pPrinterEnum", "cbBuf"),
                ("EnumPrintersW", "pPrinterEnum", "cbBuf"),
                ("GetPrinterA", "pPrinter", "cbBuf"),
                ("GetPrinterW", "pPrinter", "cbBuf"),
                ("EnumPrintProcessorDatatypesA", "pDatatypes", "cbBuf"),
                ("EnumPrintProcessorDatatypesW", "pDatatypes", "cbBuf"),
                ("EnumPrintProcessorsA", "pPrintProcessorInfo", "cbBuf"),
                ("EnumPrintProcessorsW", "pPrintProcessorInfo", "cbBuf"),
                ("EnumServicesStatusExA", "lpServices", "cbBufSize"),
                ("EnumServicesStatusExW", "lpServices", "cbBufSize"),
                ("GetTokenInformation", "TokenInformation", "TokenInformationLength"),
                ("GetCurrentPackageId", "buffer", "bufferLength"),
                ("GetPackageId", "buffer", "bufferLength"),
                ("PackageIdFromFullName", "buffer", "bufferLength"),
                ("GetCurrentPackageInfo", "buffer", "bufferLength"),
                ("GetCurrentPackageInfo2", "buffer", "bufferLength"),
                ("GetPackageInfo", "buffer", "bufferLength"),
                ("GetPackageInfo2", "buffer", "bufferLength"),
                ("GetPackageApplicationIds", "buffer", "bufferLength"),
            ];

            foreach ((string api, string buffer, string capacity) in sites)
            {
                foreach (bool allowMarshaling in new[] { false, true })
                {
                    foreach (bool includePointerOverloads in new[] { false, true })
                    {
                        yield return [api, buffer, capacity, allowMarshaling, includePointerOverloads];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verifies pointer buffers and capacities using the metadata restored from the published package.
    /// </summary>
    /// <param name="api">The annotated API to generate.</param>
    /// <param name="bufferName">The annotated buffer parameter.</param>
    /// <param name="capacityName">The buffer's capacity parameter.</param>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    /// <param name="includePointerOverloads">Whether additional pointer overloads are requested.</param>
    [Theory, MemberData(nameof(PublishedMetadataBufferCases))]
    public void PublishedMetadataBuffersRemainPointers(string api, string bufferName, string capacityName, bool allowMarshaling, bool includePointerOverloads)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with
        {
            AllowMarshaling = allowMarshaling,
            WideCharOnly = false,
            FriendlyOverloads = new() { IncludePointerOverloads = includePointerOverloads },
        });
        this.GenerateApi(api);

        MethodDeclarationSyntax[] methods = this.FindGeneratedMethod(api).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            ParameterSyntax buffer = Assert.Single(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == bufferName);
            Assert.IsType<PointerTypeSyntax>(buffer.Type);
            Assert.Empty(buffer.Modifiers);
            Assert.Contains(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == capacityName);
        });
    }

    /// <summary>
    /// Verifies that annotated real APIs retain friendly handle and scalar output projections.
    /// </summary>
    /// <param name="api">The annotated API to generate.</param>
    /// <param name="handleName">The handle parameter.</param>
    /// <param name="outputName">The scalar output parameter.</param>
    [Theory]
    [InlineData("GetPrinterW", "hPrinter", "pcbNeeded")]
    [InlineData("GetTokenInformation", "TokenHandle", "ReturnLength")]
    public void PublishedMetadataOtherParametersRemainFriendly(string api, string handleName, string outputName)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { WideCharOnly = false });
        this.GenerateApi(api);

        Assert.Contains(this.FindGeneratedMethod(api), method =>
            method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == handleName
                && parameter.Type is IdentifierNameSyntax { Identifier.ValueText: "SafeHandle" })
            && method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == outputName
                && parameter.Modifiers.Any(SyntaxKind.OutKeyword)));
    }

    /// <summary>
    /// Verifies that the published annotation does not suppress ordinary buffer projections.
    /// </summary>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    [Theory, CombinatorialData]
    public void PublishedMetadataUnannotatedBuffersStillUseSpans(bool allowMarshaling)
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator(DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling });
        this.GenerateApi("ReadFile");

        Assert.Contains(this.FindGeneratedMethod("ReadFile"), method =>
            method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "lpBuffer"
                && parameter.Type is GenericNameSyntax { Identifier.ValueText: "Span" }));
    }
}
