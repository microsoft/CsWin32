// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class FriendlyOverloadTests : GeneratorTestBase
{
    public FriendlyOverloadTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void WriteFile()
    {
        const string name = "WriteFile";
        this.Generate(name);
        Assert.Contains(this.FindGeneratedMethod(name), m => m.ParameterList.Parameters.Count == 4);
    }

    [Fact]
    public void SHGetFileInfo()
    {
        // This method uses MemorySize but for determining the size of a struct that another parameter points to.
        // We cannot know the size of that, since it may be a v1 struct, a v2 struct, etc.
        // So assert that no overload has fewer parameters or it has a Span parameter.
        const string name = "SHGetFileInfo";
        this.Generate(name);
        Assert.All(
            this.FindGeneratedMethod(name),
            m => Assert.True(
                m.ParameterList.Parameters.Count == 5 ||
                m.ParameterList.Parameters.Any(p => p.Type is GenericNameSyntax { Identifier.ValueText: "Span" })));
    }

    [Fact]
    public void SpecializedRAIIFree_ReturnValue()
    {
        const string Method = "CreateActCtx";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("ReleaseActCtxSafeHandle", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText);
    }

    [Fact]
    public void SpecializedRAIIFree_OutParameter()
    {
        const string Method = "DsGetDcOpen";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal("DsGetDcCloseWSafeHandle", Assert.IsType<QualifiedNameSyntax>(method.ParameterList.Parameters.Last().Type).Right.Identifier.ValueText);
    }

    [Fact]
    public void InAttributeOnArraysProjectedAsReadOnlySpan()
    {
        const string Method = "RmRegisterResources";
        this.GenerateApi(Method);

        MethodDeclarationSyntax method = Assert.Single(this.FindGeneratedMethod(Method), m => !IsOrContainsExternMethod(m));
        Assert.Equal(3, method.ParameterList.Parameters.Count(p => p.Type is GenericNameSyntax { Identifier.ValueText: "ReadOnlySpan" }));
    }

    [Fact]
    public void OutPWSTR_Parameters_AsSpan()
    {
        const string name = "GetWindowText";
        this.Generate(name);
        MethodDeclarationSyntax friendlyOverload = Assert.Single(this.FindGeneratedMethod(name), m => m.ParameterList.Parameters.Count == 2);
        Assert.Equal("Span<char>", friendlyOverload.ParameterList.Parameters[1].Type?.ToString());
    }

    [Theory]
    [InlineData("WSManGetSessionOptionAsString")] // Uses the reserved keyword 'string' as a parameter name
    [InlineData("RmRegisterResources")] // Parameter with PCWSTR* (an array of native strings)
    public void InterestingAPIs(string name)
    {
        this.Generate(name);
    }

    [Fact]
    public void NullCheckOfSafeHandles_ModernOverload()
    {
        var expectedCodeGen = """
            /// <inheritdoc cref="SignalObjectAndWait(winmdroot.Foundation.HANDLE, winmdroot.Foundation.HANDLE, uint, winmdroot.Foundation.BOOL)"/>
            [SupportedOSPlatform("windows5.1.2600")]
            internal static unsafe winmdroot.Foundation.WAIT_EVENT SignalObjectAndWait(SafeHandle hObjectToSignal, SafeHandle hObjectToWaitOn, uint dwMilliseconds, winmdroot.Foundation.BOOL bAlertable)
            {
                ArgumentNullException.ThrowIfNull(hObjectToSignal);
                ArgumentNullException.ThrowIfNull(hObjectToWaitOn);

                bool hObjectToSignalAddRef = false;
                bool hObjectToWaitOnAddRef = false;
                try
                {
                    hObjectToSignal.DangerousAddRef(ref hObjectToSignalAddRef);
                    winmdroot.Foundation.HANDLE hObjectToSignalLocal = (winmdroot.Foundation.HANDLE)hObjectToSignal.DangerousGetHandle();
                    hObjectToWaitOn.DangerousAddRef(ref hObjectToWaitOnAddRef);
                    winmdroot.Foundation.HANDLE hObjectToWaitOnLocal = (winmdroot.Foundation.HANDLE)hObjectToWaitOn.DangerousGetHandle();
                    winmdroot.Foundation.WAIT_EVENT __result = PInvoke.SignalObjectAndWait(hObjectToSignalLocal, hObjectToWaitOnLocal, dwMilliseconds, bAlertable);
                    return __result;
                }
                finally
                {
                    if (hObjectToSignalAddRef)
                        hObjectToSignal.DangerousRelease();
                    if (hObjectToWaitOnAddRef)
                        hObjectToWaitOn.DangerousRelease();
                }
            }
            """;

        this.compilation = this.starterCompilations["net8.0"];
        this.AssertGeneratedApiFunction(
            "SignalObjectAndWait",
            m => m.ParameterList.Parameters is [{ Type: IdentifierNameSyntax { Identifier.ValueText: "SafeHandle" } }, ..],
            expectedCodeGen);
    }

    [Fact]
    public void NullCheckOfSafeHandles_ManualNullCheck()
    {
        var expectedCodeGen = """
            /// <inheritdoc cref="SignalObjectAndWait(winmdroot.Foundation.HANDLE, winmdroot.Foundation.HANDLE, uint, winmdroot.Foundation.BOOL)"/>
            internal static unsafe winmdroot.Foundation.WAIT_EVENT SignalObjectAndWait(SafeHandle hObjectToSignal, SafeHandle hObjectToWaitOn, uint dwMilliseconds, winmdroot.Foundation.BOOL bAlertable)
            {
                if (hObjectToSignal is null)
                {
                    throw new ArgumentNullException(nameof(hObjectToSignal));
                }
                if (hObjectToWaitOn is null)
                {
                    throw new ArgumentNullException(nameof(hObjectToWaitOn));
                }
            
                bool hObjectToSignalAddRef = false;
                bool hObjectToWaitOnAddRef = false;
                try
                {
                    hObjectToSignal.DangerousAddRef(ref hObjectToSignalAddRef);
                    winmdroot.Foundation.HANDLE hObjectToSignalLocal = (winmdroot.Foundation.HANDLE)hObjectToSignal.DangerousGetHandle();
                    hObjectToWaitOn.DangerousAddRef(ref hObjectToWaitOnAddRef);
                    winmdroot.Foundation.HANDLE hObjectToWaitOnLocal = (winmdroot.Foundation.HANDLE)hObjectToWaitOn.DangerousGetHandle();
                    winmdroot.Foundation.WAIT_EVENT __result = PInvoke.SignalObjectAndWait(hObjectToSignalLocal, hObjectToWaitOnLocal, dwMilliseconds, bAlertable);
                    return __result;
                }
                finally
                {
                    if (hObjectToSignalAddRef)
                        hObjectToSignal.DangerousRelease();
                    if (hObjectToWaitOnAddRef)
                        hObjectToWaitOn.DangerousRelease();
                }
            }
            """;

        this.compilation = this.starterCompilations["net472"];
        this.AssertGeneratedApiFunction(
            "SignalObjectAndWait",
            m => m.ParameterList.Parameters is [{ Type: IdentifierNameSyntax { Identifier.ValueText: "SafeHandle" } }, ..],
            expectedCodeGen);
    }

    [Fact]
    public void OptionalHandleParameter()
    {
        var expectedCodeGen = """
            /// <inheritdoc cref="SetThreadToken(winmdroot.Foundation.HANDLE*, winmdroot.Foundation.HANDLE)"/>
            internal static unsafe winmdroot.Foundation.BOOL SetThreadToken(winmdroot.Foundation.HANDLE? Thread, SafeHandle Token)
            {
                bool TokenAddRef = false;
                try
                {
                    winmdroot.Foundation.HANDLE ThreadLocal = Thread ?? default(winmdroot.Foundation.HANDLE);
                    winmdroot.Foundation.HANDLE TokenLocal;
                    if (Token is object)
                    {
                        Token.DangerousAddRef(ref TokenAddRef);
                        TokenLocal = (winmdroot.Foundation.HANDLE)Token.DangerousGetHandle();
                    }
                    else
                    {
                        TokenLocal = (winmdroot.Foundation.HANDLE )new IntPtr(0L);
                    }
                    winmdroot.Foundation.BOOL __result = PInvoke.SetThreadToken(Thread.HasValue ? &ThreadLocal : null, TokenLocal);
                    return __result;
                }
                finally
                {
                    if (TokenAddRef)
                        Token.DangerousRelease();
                }
            }
            """;

        this.AssertGeneratedApiFunction(
            "SetThreadToken",
            m => m.ParameterList.Parameters is [_, { Type: IdentifierNameSyntax { Identifier.ValueText: "SafeHandle" } }],
            expectedCodeGen);
    }

    private void Generate(string name)
    {
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithPlatform(Platform.X64));
        this.GenerateApi(name);
    }
}
