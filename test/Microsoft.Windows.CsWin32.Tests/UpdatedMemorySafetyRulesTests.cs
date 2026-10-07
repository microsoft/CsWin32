// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Verifies the shape of the code emitted when the compilation opts into the updated memory safety rules that are in
/// preview in C# 15 / .NET 11.
/// </summary>
/// <remarks>
/// These tests assert on generated text rather than compiling it, because the Roslyn version this test project builds
/// against predates C# 15 and can neither parse the <c>safe</c> modifier and <c>unsafe(…)</c> expression nor enforce
/// the rules being satisfied.
/// </remarks>
public class UpdatedMemorySafetyRulesTests : GeneratorTestBase
{
    public UpdatedMemorySafetyRulesTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies that a P/Invoke declares its safety, which the updated rules require of every extern member, and that
    /// the answer follows whether the signature hands out a pointer.
    /// </summary>
    [Fact]
    public void ExternMethodsDeclareTheirSafety()
    {
        string code = this.GenerateAndRender("CreateFile", "GetTickCount");

        // GetTickCount takes and returns only primitives, so the projection attests that calling it is safe.
        Assert.Contains("extern safe uint GetTickCount()", code);

        // CreateFileW takes pointers, so the obligation to audit the call passes to the caller.
        Assert.Contains("extern unsafe winmdroot.Foundation.HANDLE CreateFile(winmdroot.Foundation.PCWSTR lpFileName", code);
    }

    /// <summary>
    /// Verifies that <see langword="unsafe"/> no longer appears on a type declaration, where the updated rules make it
    /// an error, and that its members carry the obligation instead.
    /// </summary>
    [Fact]
    public void TypesAreNotUnsafe()
    {
        string code = this.GenerateAndRender("PCWSTR", "IStream", "WNDPROC");

        Assert.DoesNotContain("unsafe partial struct", code);
        Assert.DoesNotContain("unsafe readonly partial struct", code);
        Assert.DoesNotContain("unsafe partial interface", code);
        Assert.DoesNotContain("unsafe delegate", code);
        Assert.DoesNotContain("unsafe struct", code);
        Assert.DoesNotContain("unsafe interface", code);
        Assert.DoesNotContain("unsafe class", code);

        // The pointer-taking conversion operators on PCWSTR are what carry the obligation now.
        Assert.Contains("static unsafe explicit operator char*(PCWSTR value)", code);
    }

    /// <summary>
    /// Verifies that a member's body gets an unsafe context of its own, since the modifier on the signature no longer
    /// establishes one.
    /// </summary>
    [Fact]
    public void MemberBodiesEstablishTheirOwnUnsafeContext()
    {
        string code = this.GenerateAndRender("PCWSTR");

        // An expression-bodied member becomes a block body so that an unsafe block can scope its operations.
        Assert.Contains("unsafe\n", code.Replace("\r\n", "\n"));
        Assert.DoesNotContain("=> value.Value;", code);
    }

    /// <summary>
    /// Verifies that a member which merely overrides a safe base member stays safe, which the updated rules require:
    /// an unsafe member may not override a safe one.
    /// </summary>
    [Fact]
    public void OverridesOfSafeMembersStaySafe()
    {
        string code = this.GenerateAndRender("PCWSTR");

        // object.Equals and object.GetHashCode are safe, so these overrides may not be marked unsafe even though
        // their bodies compare and hash a raw pointer.
        Assert.Contains("public override bool Equals(object obj)", code);
        Assert.Contains("public override int GetHashCode()", code);
        Assert.DoesNotContain("override unsafe", code);
    }

    /// <summary>
    /// Verifies that each field of a union declares its safety, which the updated rules require of every instance
    /// field in an explicitly laid out type.
    /// </summary>
    [Fact]
    public void UnionFieldsDeclareTheirSafety()
    {
        string code = this.GenerateAndRender("PROPVARIANT");

        Assert.Contains("[StructLayout(LayoutKind.Explicit)]", code);
        Assert.Contains("internal safe ushort signscale;", code);
    }

    /// <summary>
    /// Verifies that a constant whose value reaches a pointer-taking conversion operator establishes an unsafe
    /// context around the conversion, which a field initializer can only do with an unsafe expression.
    /// </summary>
    [Fact]
    public void ConstantInitializersUseUnsafeExpressions()
    {
        string code = this.GenerateAndRender("IDC_ARROW");

        // The cast has to sit inside the parentheses: the unsafe context ends at the closing one, and the conversion
        // to PCWSTR is itself caller-unsafe.
        Assert.Contains("= unsafe((winmdroot.Foundation.PCWSTR)((char*)(32512)));", code);
    }

    /// <summary>
    /// Verifies that nothing changes for a compilation that hasn't opted in.
    /// </summary>
    [Fact]
    public void OriginalModelOutputIsUnchanged()
    {
        string updated = this.GenerateAndRender("CreateFile", "PCWSTR");
        string original = this.GenerateAndRender([], "CreateFile", "PCWSTR");

        Assert.Contains("unsafe readonly partial struct PCWSTR", original);
        Assert.DoesNotContain("extern safe ", original);
        Assert.DoesNotContain("internal safe ", original);
        Assert.DoesNotContain("= unsafe(", original);
        Assert.NotEqual(original, updated);
    }

    /// <summary>
    /// Verifies how the opt-in is spelled: the compiler matches a feature flag's name without regard to case, and an
    /// explicit <see langword="false"/> turns the flag back off.
    /// </summary>
    [Theory]
    [InlineData("updated-memory-safety-rules", "true", true)]
    [InlineData("updated-memory-safety-rules", "", true)]
    [InlineData("UPDATED-MEMORY-SAFETY-RULES", "true", true)]
    [InlineData("updated-memory-safety-rules", "false", false)]
    [InlineData("some-other-feature", "true", false)]
    public void OptInIsReadFromCompilerFeatureFlags(string feature, string value, bool expectOptIn)
    {
        string code = this.GenerateAndRender([new(feature, value)], "GetTickCount");
        Assert.Equal(expectOptIn, code.Contains("extern safe uint GetTickCount()", StringComparison.Ordinal));
    }

    private string GenerateAndRender(params string[] apiNames) =>
        this.GenerateAndRender([new("updated-memory-safety-rules", "true")], apiNames);

    private string GenerateAndRender(KeyValuePair<string, string>[] features, params string[] apiNames)
    {
        this.parseOptions = this.parseOptions.WithFeatures(features);

        using SuperGenerator generator = this.CreateGenerator();
        foreach (string apiName in apiNames)
        {
            Assert.True(generator.TryGenerate(apiName, CancellationToken.None), $"Failed to generate {apiName}.");
        }

        StringBuilder builder = new();
        foreach (KeyValuePair<string, CompilationUnitSyntax> unit in generator.GetCompilationUnits(CancellationToken.None))
        {
            builder.AppendLine(unit.Value.ToFullString());
        }

        string code = builder.ToString();
        this.logger.WriteLine(code);
        return code;
    }
}
