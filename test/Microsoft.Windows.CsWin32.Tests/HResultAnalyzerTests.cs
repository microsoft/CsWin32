// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Tests the analyzer that reports HRESULT results that the caller ignores.
/// </summary>
public class HResultAnalyzerTests : GeneratorTestBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HResultAnalyzerTests"/> class.
    /// </summary>
    /// <param name="logger">The test output.</param>
    public HResultAnalyzerTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies the diagnostic descriptor.
    /// </summary>
    [Fact]
    public void GetSupportedDiagnostics_DefaultConfiguration_ReturnsEnabledPInvoke019Warning()
    {
        DiagnosticDescriptor descriptor = Assert.Single(new HResultAnalyzer().SupportedDiagnostics);
        Assert.Equal("PInvoke019", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
    }

    /// <summary>
    /// Verifies that intentional uses of an HRESULT do not produce a diagnostic.
    /// </summary>
    /// <param name="member">The member that uses the HRESULT.</param>
    [Theory]
    [InlineData("void M() { Work().ThrowOnFailure(); }")]
    [InlineData("void M() { if (Work().Succeeded) { } }")]
    [InlineData("void M() { if (Work().Failed) return; }")]
    [InlineData("void M() { HRESULT hr = Work(); if (hr == HRESULT.S_FALSE) { } }")]
    [InlineData("void M() { HRESULT hr = Work(); switch (hr.Value) { case 0: break; default: hr.ThrowOnFailure(); break; } }")]
    [InlineData("void M() { switch (Work()) { case { Failed: true }: break; } }")]
    [InlineData("int M() => Work() switch { { Failed: true } => 1, _ => 0 };")]
    [InlineData("void M() { _ = Work(); }")]
    [InlineData("void M() { Log(Work()); }")]
    [InlineData("void M() { this.LastResult = Work(); }")]
    [InlineData("void M() { this.results.Add(Work()); }")]
    [InlineData("HRESULT M() { return Work(); }")]
    [InlineData("HRESULT M() => Work();")]
    [InlineData("HRESULT M() { HRESULT hr = Work(); return hr; }")]
    [InlineData("void M() { HRESULT hr = Work(); if (hr.Failed) { return; } }")]
    [InlineData("bool M() => Work().Value < 0;")]
    [InlineData("void M() { LogValue(Work().Value); }")]
    [InlineData("void M() { int value = Work().Value; LogValue(value); }")]
    [InlineData("void M() { uint value = Work(); LogValue(value); }")]
    [InlineData("void M() { HRESULT hr = Work(); HRESULT copy = hr; copy.ThrowOnFailure(); }")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); if (c) { hr.ThrowOnFailure(); } else { Log(hr); } }")]
    [InlineData("void M(bool c) { HRESULT hr = c ? Work() : HRESULT.S_OK; hr.ThrowOnFailure(); }")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); hr.ThrowOnFailure(); hr = c ? InstanceWork() : HRESULT.S_OK; hr.ThrowOnFailure(); }")]
    [InlineData("void M() { HRESULT hr = Work(); hr = hr.Failed ? InstanceWork() : HRESULT.S_OK; hr.ThrowOnFailure(); }")]
    [InlineData("void M(bool c) { HRESULT hr = HRESULT.S_OK; hr = c ? Work() : InstanceWork(); hr.ThrowOnFailure(); }")]
    [InlineData("void M(bool c) { this.LastResult = c ? Work() : InstanceWork(); }")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); if (c) { hr.ThrowOnFailure(); return; } Log(hr); }")]
    [InlineData("void M() { HRESULT hr; while ((hr = Work()).Succeeded) { } }")]
    [InlineData("void M() { HRESULT hr = Work(); try { } finally { hr.ThrowOnFailure(); } }")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); if (c) { throw new InvalidOperationException(); } hr.ThrowOnFailure(); }")]
    [InlineData("void M() { HRESULT hr = Work(); Action a = () => hr.ThrowOnFailure(); a(); }")]
    [InlineData("void M() { Action a = () => { HRESULT hr = Work(); hr.ThrowOnFailure(); }; a(); }")]
    [InlineData("void M() { Local(); void Local() { HRESULT hr = Work(); Log(hr); } }")]
    [InlineData("void M() { if (Work() is { Failed: true }) { } }")]
    [InlineData("void M() { Func<HRESULT> f = Work; f().ThrowOnFailure(); }")]
    [InlineData("void M() { PInvoke.CoCreateGuid(out Guid id).ThrowOnFailure(); }")]
    [InlineData("void M() { HRESULT hr = Work(); hr.ThrowOnFailure(); hr = Work(); Log(hr); }")]
    [InlineData("void M(HRESULT hr) { hr = Work(); Log(hr); }")]
    [InlineData("HRESULT P => Work();")]
    [InlineData("internal static HRESULT Initial = Work();")]
    [InlineData("void M(out HRESULT hr) { hr = Work(); }")]
    [InlineData("void M(Usage other) { other?.InstanceWork().ThrowOnFailure(); }")]
    [InlineData("void M(Usage other) { HRESULT? hr = other?.InstanceWork(); hr?.ThrowOnFailure(); }")]
    [InlineData("string M() => $\"{Work()}\";")]
    [InlineData("void M() { try { Work().ThrowOnFailure(); } catch (Exception) { } }")]
    [InlineData("async Task M() { HRESULT hr = Work(); await Task.Yield(); hr.ThrowOnFailure(); }")]
    [InlineData("IEnumerable<int> M() { HRESULT hr = Work(); yield return hr.Value; }")]
    [InlineData("void M() { using (var stream = new System.IO.MemoryStream()) { HRESULT hr = Work(); hr.ThrowOnFailure(); } }")]
    [InlineData("void M(int[] values) { foreach (int v in values) { HRESULT hr = Work(); if (hr.Failed) { break; } } }")]
    public async Task Analyze_HResultIsUsedOrExplicitlyDiscarded_DoesNotReportDiagnostic(string member)
    {
        this.GenerateFixture();
        this.compilation = this.AddCode(CreateUsage(member));
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(await this.AnalyzeAsync());
    }

    /// <summary>
    /// Verifies that each HRESULT that is unused on a normal path produces one warning at its invocation.
    /// </summary>
    /// <param name="member">The member that does not use the HRESULT.</param>
    /// <param name="invocation">The source text of the reported invocation.</param>
    /// <param name="name">The name in the diagnostic message.</param>
    [Theory]
    [InlineData("void M() { Work(); }", "Work()", "Work")]
    [InlineData("void M() { PInvoke.CoCreateGuid(out Guid id); }", "PInvoke.CoCreateGuid(out Guid id)", "CoCreateGuid")]
    [InlineData("void M() { HRESULT hr = Work(); }", "Work()", "Work")]
    [InlineData("void M() { HRESULT hr = Work(); hr = HRESULT.S_OK; hr.ThrowOnFailure(); }", "Work()", "Work")]
    [InlineData("void M() { int value = Work().Value; }", "Work()", "Work")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); if (c) { hr.ThrowOnFailure(); } }", "Work()", "Work")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); if (c) { return; } hr.ThrowOnFailure(); }", "Work()", "Work")]
    [InlineData("void M(bool c) { while (c) { HRESULT hr = Work(); } }", "Work()", "Work")]
    [InlineData("void M() { Action a = () => Work(); a(); }", "Work()", "Work")]
    [InlineData("void M() { Action a = () => { HRESULT hr = Work(); }; a(); }", "Work()", "Work")]
    [InlineData("void M() { Local(); void Local() { Work(); } }", "Work()", "Work")]
    [InlineData("void M() { HRESULT hr = Work(); HRESULT copy = hr; }", "Work()", "Work")]
    [InlineData("void M() { uint value = Work(); }", "Work()", "Work")]
    [InlineData("void M(Usage other) { other?.InstanceWork(); }", ".InstanceWork()", "InstanceWork")]
    [InlineData("void M() { Func<HRESULT> f = Work; f(); }", "f()", "f")]
    [InlineData("void M(bool c) { HRESULT hr = Work(); try { if (c) { return; } } finally { } hr.ThrowOnFailure(); }", "Work()", "Work")]
    [InlineData("void M(HRESULT hr) { hr = Work(); }", "Work()", "Work")]
    [InlineData("int P { get { Work(); return 0; } }", "Work()", "Work")]
    [InlineData("Usage() { Work(); }", "Work()", "Work")]
    [InlineData("void M() { try { Work(); } catch (Exception) { } }", "Work()", "Work")]
    [InlineData("void M() { try { } finally { Work(); } }", "Work()", "Work")]
    [InlineData("async Task M() { HRESULT hr = Work(); await Task.Yield(); }", "Work()", "Work")]
    [InlineData("IEnumerable<int> M() { HRESULT hr = Work(); yield return 0; }", "Work()", "Work")]
    [InlineData("void M(int[] values) { foreach (int v in values) { Work(); } }", "Work()", "Work")]
    [InlineData("void M() { HRESULT? hr = Work(); }", "Work()", "Work")]
    [InlineData("void M(bool c) { HRESULT hr = HRESULT.S_OK; hr = c ? Work() : HRESULT.S_OK; }", "Work()", "Work")]
    [InlineData("void M(bool c) { int value = 0; value = c ? Work().Value : 0; }", "Work()", "Work")]
    public async Task Analyze_HResultIsUnusedOnANormalPath_ReportsSingleWarningAtInvocation(string member, string invocation, string name)
    {
        this.GenerateFixture();
        this.compilation = this.AddCode(CreateUsage(member));
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Diagnostic diagnostic = Assert.Single(await this.AnalyzeAsync());
        Assert.Equal("PInvoke019", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(invocation, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Equal($"Check the HRESULT returned by '{name}' on every path, or discard it with '_ ='", diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies that conditional overwrites report each invocation whose result is not checked.
    /// </summary>
    [Theory]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); while (c) { hr = InstanceWork(); } hr.ThrowOnFailure(); }",
        "Work,InstanceWork")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); if (c) { hr = InstanceWork(); } hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); hr = c ? InstanceWork() : HRESULT.S_OK; hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); hr = c ? InstanceWork() : HRESULT.S_OK; }",
        "Work,InstanceWork")]
    [InlineData(
        "void M(HRESULT hr, bool c) { hr = Work(); hr = c ? InstanceWork() : HRESULT.S_OK; hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); hr = c ? hr : InstanceWork(); hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c, bool d) { HRESULT hr = Work(); hr = c ? (d ? InstanceWork() : HRESULT.S_OK) : HRESULT.S_FALSE; hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); (hr = c ? InstanceWork() : HRESULT.S_OK).ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); hr = c ? InstanceWork() : throw new InvalidOperationException(); hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(Usage other) { HRESULT hr = Work(); hr = other?.InstanceWork() ?? HRESULT.S_OK; hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { HRESULT hr = Work(); hr = c switch { true => InstanceWork(), false => HRESULT.S_OK }; hr.ThrowOnFailure(); }",
        "Work")]
    [InlineData(
        "void M(bool c) { int value = Work().Value; value = c ? InstanceWork().Value : 0; LogValue(value); }",
        "Work")]
    public async Task Analyze_HResultIsConditionallyOverwritten_ReportsEachUnobservedInvocation(
        string member,
        string expectedNames)
    {
        this.GenerateFixture();
        this.compilation = this.AddCode(CreateUsage(member));
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        string[] actualNames = (await this.AnalyzeAsync())
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => diagnostic.GetMessage().Split('\'')[1])
            .ToArray();
        Assert.Equal(expectedNames.Split(','), actualNames);
    }

    /// <summary>
    /// Verifies that a checked HRESULT in top-level statements does not produce a diagnostic.
    /// </summary>
    [Fact]
    public async Task Analyze_TopLevelHResultIsChecked_DoesNotReportDiagnostic()
    {
        Assert.Empty(await this.AnalyzeTopLevelStatementsAsync("HRESULT hr = Work(); hr.ThrowOnFailure();"));
    }

    /// <summary>
    /// Verifies that an ignored HRESULT in top-level statements produces one diagnostic.
    /// </summary>
    /// <param name="statements">The top-level statements.</param>
    [Theory]
    [InlineData("HRESULT hr = Work();")]
    [InlineData("Work();")]
    public async Task Analyze_TopLevelHResultIsIgnored_ReportsSingleDiagnostic(string statements)
    {
        Assert.Single(await this.AnalyzeTopLevelStatementsAsync(statements));
    }

    /// <summary>
    /// Verifies that a struct named HRESULT that CsWin32 did not generate is ignored.
    /// </summary>
    [Fact]
    public async Task Analyze_IgnoredHResultIsNotCsWin32HResult_DoesNotReportDiagnostic()
    {
        this.GenerateFixture();
        this.compilation = this.AddCode("""
            namespace Other
            {
                struct HRESULT { }

                static class Usage
                {
                    static HRESULT Work() => default;

                    static void M() { Work(); }
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(await this.AnalyzeAsync());
    }

    private static string CreateUsage(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Windows.Win32;
        using Windows.Win32.Foundation;

        class Usage
        {
            internal HRESULT LastResult { get; set; }
            internal readonly List<HRESULT> results = new();

            static HRESULT Work() => HRESULT.S_OK;

            internal HRESULT InstanceWork() => HRESULT.S_FALSE;

            static void Log(HRESULT hr) { }

            static void LogValue(long value) { }

            {{member}}
        }
        """;

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync() => this.compilation.WithAnalyzers(
        ImmutableArray.Create<DiagnosticAnalyzer>(new HResultAnalyzer())).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

    private Task<ImmutableArray<Diagnostic>> AnalyzeTopLevelStatementsAsync(string statements)
    {
        this.GenerateFixture();
        this.compilation = this.compilation.WithOptions(this.compilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
        this.compilation = this.AddCode($$"""
            using Windows.Win32.Foundation;
            {{statements}}
            static HRESULT Work() => HRESULT.S_OK;
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        return this.AnalyzeAsync();
    }

    private void GenerateFixture()
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.generator = this.CreateGenerator();
        foreach (string api in new[] { "CoCreateGuid", "S_OK", "S_FALSE" })
        {
            Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
        }

        this.CollectGeneratedCode(this.generator);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }
}
