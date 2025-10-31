// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32.Storage.FileSystem;
using static Windows.Win32.PInvoke;

#pragma warning disable SA1201, CS0649

namespace GenerationSandbox.BuildTask.Tests;

[Trait("WindowsOnly", "true")]
public partial class SpanOverloadTests
{
    [Fact]
    public void CanCallGetFileVersionApis()
    {
        var appName = GetAppName("C:\\windows\\notepad.exe");
        Assert.True(appName is not null);
    }

    internal struct LANGANDCODEPAGE
    {
        public ushort Language;
        public ushort Codepage;
    }

    internal static unsafe string GetAppName(string processPath)
    {
        uint infoSize = GetFileVersionInfoSizeEx(GET_FILE_VERSION_INFO_FLAGS.FILE_VER_GET_NEUTRAL, processPath, out uint handle);
        if (infoSize > 0)
        {
            // Try whatever language is supported
            // https://learn.microsoft.com/en-us/windows/win32/api/winver/nf-winver-verqueryvaluew
            Span<byte> versionInfo = new byte[infoSize];
            if (GetFileVersionInfoEx(GET_FILE_VERSION_INFO_FLAGS.FILE_VER_GET_NEUTRAL, processPath, versionInfo))
            {
                fixed (void* pvVersionInfo = versionInfo)
                {
                    if (VerQueryValue(pvVersionInfo, @"\VarFileInfo\Translation", out void* pvLangCodePage, out uint cbTranslate) &&
                        pvLangCodePage is not null &&
                        cbTranslate >= sizeof(LANGANDCODEPAGE))
                    {
                        LANGANDCODEPAGE* langCodePage = (LANGANDCODEPAGE*)pvLangCodePage;
                        string path = $@"\StringFileInfo\{langCodePage->Language.ToString("x4", CultureInfo.InvariantCulture.NumberFormat)}{langCodePage->Codepage.ToString("x4", CultureInfo.InvariantCulture.NumberFormat)}\FileDescription";
                        if (VerQueryValue(pvVersionInfo, path, out void* descPtr, out uint desLen) &&
                            descPtr is not null &&
                            desLen > 0)
                        {
                            return Marshal.PtrToStringAuto(new(descPtr)) ?? string.Empty;
                        }
                    }
                }
            }
        }

        // Fallback to the process name
        return Path.GetFileNameWithoutExtension(processPath);
    }
}
