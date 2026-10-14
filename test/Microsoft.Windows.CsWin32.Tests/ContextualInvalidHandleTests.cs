// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Verifies invalid values supplied by producer return and output annotations, rather than fabricated typedefs.
/// </summary>
public class ContextualInvalidHandleTests : GeneratorTestBase
{
    private const string MetadataSource = """
        using System;
        using System.Runtime.InteropServices;
        using Windows.Win32.Foundation.Metadata;
        namespace Windows.Win32.Foundation.Metadata
        {
            public sealed class RAIIFreeAttribute : Attribute { public RAIIFreeAttribute(string name) { } }
            [AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Parameter | AttributeTargets.Struct, AllowMultiple = true)]
            public sealed class InvalidHandleValueAttribute : Attribute { public InvalidHandleValueAttribute(long value) { } }
        }
        namespace Windows.Win32.Test
        {
            public static unsafe class Apis
            {
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void CloseThing(void* resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: RAIIFree("CloseThing"), InvalidHandleValue(0), InvalidHandleValue(-1)]
                public static extern void* OpenThing();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern int CreateThing([Out, RAIIFree("CloseThing"), InvalidHandleValue(0), InvalidHandleValue(-1)] void** resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: RAIIFree("CloseThing"), InvalidHandleValue(0)]
                public static extern void* OpenZeroInvalid();
            }
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextualInvalidHandleTests"/> class.
    /// </summary>
    /// <param name="logger">The test output.</param>
    public ContextualInvalidHandleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Exercises invalid handles, output ownership and disposal while a typed lease is alive.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextualInvalidValuesAndLeaseLifetime(bool allowMarshaling)
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string path = AssociatedEnumTests.EmitMetadata(MetadataSource, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling, Public = true }, this.compilation, this.parseOptions);
            foreach (string api in new[] { "OpenThing", "CreateThing", "OpenZeroInvalid" })
            {
                Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
            }

            this.CollectGeneratedCode(this.generator);
        }
        finally
        {
            File.Delete(path);
        }

        IMethodSymbol open = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("OpenThing_SafeHandle").OfType<IMethodSymbol>());
        string ownerName = open.ReturnType.Name;
        IMethodSymbol create = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("CreateThing").OfType<IMethodSymbol>(), method => method.Parameters[0].RefKind == RefKind.Out);
        Assert.True(SymbolEqualityComparer.Default.Equals(open.ReturnType, create.Parameters[0].Type));
        IMethodSymbol zero = Assert.Single(this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!.GetMembers("OpenZeroInvalid_SafeHandle").OfType<IMethodSymbol>());
        Assert.False(SymbolEqualityComparer.Default.Equals(open.ReturnType, zero.ReturnType));
        string zeroOwnerName = zero.ReturnType.Name;

        foreach (SyntaxTree tree in this.compilation.SyntaxTrees.ToArray())
        {
            MethodDeclarationSyntax[] nativeMethods = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => method.Modifiers.Any(SyntaxKind.ExternKeyword) && method.Identifier.ValueText is "CloseThing" or "CreateThing").ToArray();
            SyntaxNode rewritten = tree.GetRoot(TestContext.Current.CancellationToken).ReplaceNodes(nativeMethods, (original, current) => current.WithAttributeLists(default)
                .WithModifiers(TokenList(current.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ExternKeyword))))
                .WithBody((BlockSyntax)ParseStatement(original.Identifier.ValueText == "CloseThing" ? "{ RuntimeUsage.Frees++; }" : "{ *resource = (void*)(-1); return -1; }"))
                .WithSemicolonToken(default));
            this.compilation = this.compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(rewritten.ToFullString(), this.parseOptions, tree.FilePath, cancellationToken: TestContext.Current.CancellationToken));
        }

        this.compilation = this.AddCode($$"""
            using System;
            using Windows.Win32;
            /// <summary>Executes contextual owners and leases.</summary>
            public static unsafe class RuntimeUsage
            {
                /// <summary>Counts native cleanup calls.</summary>
                public static int Frees;
                /// <summary>Verifies invalid values and reference balance.</summary>
                public static void Run()
                {
                    using (var invalid = new {{ownerName}}((IntPtr)(-1)))
                    {
                        if (!invalid.IsInvalid) throw new Exception("Lost contextual -1 invalid value.");
                    }
                    using (var invalid = new {{ownerName}}(IntPtr.Zero))
                    {
                        if (!invalid.IsInvalid) throw new Exception("Lost contextual zero invalid value.");
                    }
                    if (Frees != 0) throw new Exception("Released an invalid handle.");
                    using (var valid = new {{zeroOwnerName}}((IntPtr)(-1)))
                    {
                        if (valid.IsInvalid) throw new Exception("Conflated contextual invalid-value profiles.");
                    }
                    if (Frees != 1) throw new Exception("Failed to release a valid handle.");
                    var owner = new {{ownerName}}((IntPtr)42);
                    using (var lease = owner.Lease())
                    {
                        owner.Dispose();
                        if (Frees != 1 || (nint)lease.Value != 42) throw new Exception("Released a borrowed resource.");
                    }
                    if (Frees != 2) throw new Exception("Unbalanced lease release.");
                    PInvoke.CreateThing(out {{ownerName}} output);
                    using (output)
                    {
                        if (!output.IsInvalid) throw new Exception("Lost invalid output value.");
                    }
                    if (Frees != 2) throw new Exception("Released an invalid output.");
                }
                static void Raw()
                {
                    void* resource;
                    PInvoke.CreateThing(&resource);
                    void* opened = PInvoke.OpenThing();
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(await this.compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SafeHandleAnalyzer())).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken));
        using MemoryStream stream = new();
        var result = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assembly.Load(stream.ToArray()).GetType("RuntimeUsage")!.GetMethod("Run")!.Invoke(null, null);
    }

    /// <summary>
    /// Reuses a BCL owner only when its invalid-value contract matches the producing annotation.
    /// </summary>
    [Fact]
    public void ContextualInvalidValuesOverrideBclOwner()
    {
        this.compilation = this.starterCompilations["net8.0-x64"];
        string source = MetadataSource.Replace(
            "public sealed class RAIIFreeAttribute",
            "public sealed class NativeTypedefAttribute : Attribute { } public sealed class RAIIFreeAttribute",
            StringComparison.Ordinal) + """

            namespace Windows.Win32.Foundation
            {
                [NativeTypedef, RAIIFree("CloseHandle"), InvalidHandleValue(0), InvalidHandleValue(-1)]
                public unsafe struct HANDLE { public void* Value; }
                public static class Apis
                {
                    [DllImport("fixture.dll", ExactSpelling = true)]
                    public static extern int CloseHandle(HANDLE resource);
                    [DllImport("fixture.dll", ExactSpelling = true)]
                    [return: RAIIFree("CloseHandle"), InvalidHandleValue(0), InvalidHandleValue(-1)]
                    public static extern HANDLE OpenBcl();
                    [DllImport("fixture.dll", ExactSpelling = true)]
                    [return: RAIIFree("CloseHandle"), InvalidHandleValue(0)]
                    public static extern HANDLE OpenCustom();
                }
            }
            """;
        string path = AssociatedEnumTests.EmitMetadata(source, this.compilation, this.parseOptions);
        try
        {
            this.generator = new Generator(path, null, [], DefaultTestGeneratorOptions with { Public = true }, this.compilation, this.parseOptions);
            Assert.True(this.generator.TryGenerate("OpenBcl", TestContext.Current.CancellationToken));
            Assert.True(this.generator.TryGenerate("OpenCustom", TestContext.Current.CancellationToken));
            this.CollectGeneratedCode(this.generator);
        }
        finally
        {
            File.Delete(path);
        }

        INamedTypeSymbol pinvoke = this.compilation.GetTypeByMetadataName("Windows.Win32.PInvoke")!;
        IMethodSymbol bcl = Assert.Single(pinvoke.GetMembers("OpenBcl_SafeHandle").OfType<IMethodSymbol>());
        Assert.Equal("Microsoft.Win32.SafeHandles.SafeFileHandle", bcl.ReturnType.ToDisplayString());
        IMethodSymbol custom = Assert.Single(pinvoke.GetMembers("OpenCustom_SafeHandle").OfType<IMethodSymbol>());
        Assert.Equal("Windows.Win32.CloseHandleInvalid0SafeHandle", custom.ReturnType.ToDisplayString());
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }
}
