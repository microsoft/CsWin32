// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Windows.Win32.Foundation;

public class StringTests
{
    private const string ExpectedString = "Hello";
    private const string ExpectedStringList = "Hello\0Goodbye\0";
    private const string NoisyString = $"{ExpectedStringList}\0garbage";
    private static readonly int ExpectedStringLength = ExpectedString.Length;
    private static readonly int ExpectedStringListLength = ExpectedStringList.Length;
    private static readonly byte[] NoisyStringUtf8 = Encoding.UTF8.GetBytes(NoisyString);
    private static readonly char[] NoisyStringUtf16 = NoisyString.ToCharArray();
    private static readonly byte[] NullUtf8 = new byte[] { 0, 5 };
    private static readonly char[] NullUtf16 = new char[] { '\0', 'a' };

    [Fact]
    public unsafe void PCSTR_Length()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringLength, new PCSTR(pStr).Length);
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Equal(0, new PCSTR(pEmpty).Length);
            Assert.Equal(0, new PCSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PCWSTR_Length()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringLength, new PCWSTR(pStr).Length);
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Equal(0, new PCWSTR(pEmpty).Length);
            Assert.Equal(0, new PCWSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PCZZSTR_Length()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringListLength, new PCZZSTR(pStr).Length);
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Equal(0, new PCZZSTR(pEmpty).Length);
            Assert.Equal(0, new PCZZSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PCZZWSTR_Length()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringListLength, new PCZZWSTR(pStr).Length);
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Equal(0, new PCZZWSTR(pEmpty).Length);
            Assert.Equal(0, new PCZZWSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PSTR_Length()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringLength, new PSTR(pStr).Length);
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Equal(0, new PSTR(pEmpty).Length);
            Assert.Equal(0, new PSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PWSTR_Length()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringLength, new PWSTR(pStr).Length);
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Equal(0, new PWSTR(pEmpty).Length);
            Assert.Equal(0, new PWSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PZZSTR_Length()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringListLength, new PZZSTR(pStr).Length);
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Equal(0, new PZZSTR(pEmpty).Length);
            Assert.Equal(0, new PZZSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PZZWSTR_Length()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringListLength, new PZZWSTR(pStr).Length);
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Equal(0, new PZZWSTR(pEmpty).Length);
            Assert.Equal(0, new PZZWSTR(null).Length);
        }
    }

    [Fact]
    public unsafe void PCSTR_ToString()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedString, new PCSTR(pStr).ToString());
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Same(string.Empty, new PCSTR(pEmpty).ToString());
            Assert.Null(new PCSTR(null).ToString());
        }
    }

    [Fact]
    public unsafe void PCWSTR_ToString()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedString, new PCWSTR(pStr).ToString());
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Same(string.Empty, new PCWSTR(pEmpty).ToString());
            Assert.Null(new PCWSTR(null).ToString());
        }
    }

    [Fact]
    public unsafe void PCZZSTR_ToString()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringList, new PCZZSTR(pStr).ToString());
        }
    }

    [Fact]
    public unsafe void PCZZWSTR_ToString()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringList, new PCZZWSTR(pStr).ToString());
        }
    }

    [Fact]
    public unsafe void PSTR_ToString()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedString, new PSTR(pStr).ToString());
        }

        fixed (byte* pEmpty = NullUtf8)
        {
            Assert.Same(string.Empty, new PSTR(pEmpty).ToString());
            Assert.Null(new PSTR(null).ToString());
        }
    }

    [Fact]
    public unsafe void PWSTR_ToString()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedString, new PWSTR(pStr).ToString());
        }

        fixed (char* pEmpty = NullUtf16)
        {
            Assert.Same(string.Empty, new PWSTR(pEmpty).ToString());
            Assert.Null(new PWSTR(null).ToString());
        }
    }

    [Fact]
    public unsafe void PZZSTR_ToString()
    {
        fixed (byte* pStr = NoisyStringUtf8)
        {
            Assert.Equal(ExpectedStringList, new PZZSTR(pStr).ToString());
        }
    }

    [Fact]
    public unsafe void PZZWSTR_ToString()
    {
        fixed (char* pStr = NoisyStringUtf16)
        {
            Assert.Equal(ExpectedStringList, new PZZWSTR(pStr).ToString());
        }
    }

    [Fact]
    public unsafe void ImplicitConversion()
    {
        byte* pb = (byte*)1;
        char* pch = (char*)1;

        PSTR str = new(pb);
        PWSTR wstr = new(pch);
        PZZSTR zzstr = new(pb);
        PZZWSTR zzwstr = new(pch);

        PCSTR cstr = str;
        PCWSTR cwstr = wstr;
        PCZZSTR czzstr = zzstr;
        PCZZWSTR czzwstr = zzwstr;

        Assert.Equal(str, cstr);
        Assert.Equal(wstr, cwstr);
        Assert.Equal(zzstr, czzstr);
        Assert.Equal(zzwstr, czzwstr);
    }
}
