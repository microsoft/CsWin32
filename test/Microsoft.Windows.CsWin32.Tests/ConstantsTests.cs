// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class ConstantsTests : GeneratorTestBase
{
    public ConstantsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory]
    [InlineData("SECURITY_NULL_SID_AUTHORITY")] // SID_IDENTIFIER_AUTHORITY with byte[6] inline array
    [InlineData("g_wszStreamBufferRecordingDuration")] // string
    [InlineData("HWND_BOTTOM")] // A constant typed as a typedef'd struct
    [InlineData("D2D1_DEFAULT_FLATTENING_TOLERANCE")] // a float constant
    [InlineData("WIA_CATEGORY_FINISHED_FILE")] // GUID constant
    [InlineData("DEVPKEY_MTPBTH_IsConnected")] // DEVPROPKEY constant
    [InlineData("PKEY_AudioEndpoint_FormFactor")] // PROPERTYKEY constant
    [InlineData("X509_CERT")] // A constant defined as PCSTR
    [InlineData("RT_CURSOR")] // PCWSTR constant
    [InlineData("HBMMENU_POPUP_RESTORE")] // A HBITMAP handle as a constant
    [InlineData("CONDITION_VARIABLE_INIT")] // A 0 constant typed void*
    [InlineData("DEVPKEY_Bluetooth_DeviceAddress")] // WDK constant defined as a type from the SDK
    public void InterestingConstants(string name)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.GenerateApi(name);
    }
}
