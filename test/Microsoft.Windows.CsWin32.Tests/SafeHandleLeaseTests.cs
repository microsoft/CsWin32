// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Tests user-visible ownership projections, lease behavior, and lifetime diagnostics.
/// </summary>
public class SafeHandleLeaseTests : GeneratorTestBase
{
    private const string MetadataSource = """
        using System;
        using System.Runtime.InteropServices;
        using Windows.Win32.Foundation.Metadata;

        namespace Windows.Win32.Foundation.Metadata
        {
            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class NativeTypedefAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
            public sealed class InvalidHandleValueAttribute : Attribute
            {
                public InvalidHandleValueAttribute(long value) { }
            }
            [AttributeUsage(AttributeTargets.Struct | AttributeTargets.ReturnValue | AttributeTargets.Parameter)]
            public sealed class RAIIFreeAttribute : Attribute
            {
                public RAIIFreeAttribute(string method) { }
            }
            [AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Parameter)]
            public sealed class FreeWithAttribute : Attribute
            {
                public FreeWithAttribute(string method) { }
            }
        }
        namespace Windows.Win32.Foundation
        {
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct HANDLE { public void* Value; }
        }
        namespace Windows.Win32.Other
        {
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct DUPLICATE_RESOURCE { public void* Value; }
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct HANDLE { public void* Value; }
        }
        namespace Windows.Win32.Test
        {
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct RESOURCE_A { public void* Value; }
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct RESOURCE_B { public void* Value; }
            [NativeTypedef, InvalidHandleValue(0)]
            public struct UNSIGNED_RESOURCE { public UIntPtr Value; }
            [NativeTypedef, InvalidHandleValue(0)]
            public struct UNSIGNED32_RESOURCE { public uint Value; }
            [NativeTypedef, InvalidHandleValue(0)]
            public unsafe struct DUPLICATE_RESOURCE { public void* Value; }

            public static unsafe class Apis
            {
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeShared))]
                public static extern RESOURCE_A CreateResource();
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeShared))]
                public static extern RESOURCE_B CreateOtherResource();
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeShared))]
                public static extern void* CreatePointer();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern int AcquirePointer([Out, __Cleanup(nameof(FreeShared))] void** resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern int AcquireResource([Out, __Cleanup(nameof(FreeShared))] RESOURCE_A* resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void FreeShared(void* resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void ConsumeResource([In] RESOURCE_A resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void ConsumeHandle([In] Windows.Win32.Foundation.HANDLE resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void ConsumeOtherHandle([In] Windows.Win32.Other.HANDLE resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void ConsumePointer([In] void* resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeOtherResource))]
                public static extern RESOURCE_A IncorrectCleanup();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void FreeOtherResource(RESOURCE_B resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeContext))]
                public static extern RESOURCE_A CreateWithContext();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern bool FreeContext(RESOURCE_A resource, int context);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeUnsigned))]
                public static extern UNSIGNED_RESOURCE CreateUnsigned();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void FreeUnsigned(UNSIGNED_RESOURCE resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeUnsigned32))]
                public static extern UNSIGNED32_RESOURCE CreateUnsigned32();
                [DllImport("fixture.dll", ExactSpelling = true)]
                public static extern void FreeUnsigned32(UNSIGNED32_RESOURCE resource);
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeShared))]
                public static extern DUPLICATE_RESOURCE CreateDuplicate();
                [DllImport("fixture.dll", ExactSpelling = true)]
                [return: __Cleanup(nameof(FreeShared))]
                public static extern Windows.Win32.Other.DUPLICATE_RESOURCE CreateOtherDuplicate();
            }
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeHandleLeaseTests"/> class.
    /// </summary>
    /// <param name="logger">The test output.</param>
    public SafeHandleLeaseTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies native value types and caller compatibility when several resource types share a cleanup function.
    /// </summary>
    /// <param name="attribute">The contextual cleanup attribute.</param>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    [Theory, CombinatorialData]
    public void ContextualOwnersPreserveTheirNativeTypes(
        [CombinatorialValues("RAIIFree", "FreeWith")] string attribute, bool allowMarshaling)
    {
        this.GenerateFixture(attribute, allowMarshaling, "CreateResource", "CreateOtherResource", "CreatePointer", "AcquirePointer", "AcquireResource", "ConsumeResource", "ConsumePointer");
        this.compilation = this.AddCode("""
            using Windows.Win32;
            using Windows.Win32.Test;
            static unsafe class Usage
            {
                static void Use(FreeSharedRESOURCE_ASafeHandle a, FreeSharedRESOURCE_BSafeHandle b, FreeSharedSafeHandle pointer)
                {
                    using var leaseA = a.Lease();
                    using var leaseB = b.Lease();
                    using var leasePointer = pointer.Lease();
                    RESOURCE_A valueA = leaseA.Value;
                    RESOURCE_B valueB = leaseB.Value;
                    void* valuePointer = leasePointer.Value;
                    FreeSharedSafeHandle compatibleWithExistingOwner = a;
                    PInvoke.ConsumeResource(leaseA.Value);
                    PInvoke.ConsumePointer(leasePointer.Value);
                    PInvoke.AcquirePointer(out FreeSharedSafeHandle ownedPointer);
                    PInvoke.AcquireResource(out FreeSharedRESOURCE_ASafeHandle ownedA);
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.DoesNotContain(this.FindGeneratedMethod("ConsumeResource"), method => method.ParameterList.Parameters[0].Type?.ToString() == "SafeHandle");
        Assert.DoesNotContain(this.FindGeneratedMethod("ConsumePointer"), method => method.ParameterList.Parameters.Any(parameter => parameter.Type?.ToString() == "SafeHandle"));
        foreach (string name in new[] { "FreeSharedSafeHandle", "FreeSharedRESOURCE_ASafeHandle", "FreeSharedRESOURCE_BSafeHandle" })
        {
            INamedTypeSymbol? owner = this.compilation.GetTypeByMetadataName("Windows.Win32." + name);
            Assert.NotNull(owner);
            IPropertySymbol ownerValue = Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(owner.GetMembers("DangerousValue")));
            INamedTypeSymbol lease = Assert.Single(owner.GetTypeMembers("LeaseScope"));
            IPropertySymbol leaseValue = Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(lease.GetMembers("Value")));
            Assert.True(SymbolEqualityComparer.Default.Equals(ownerValue.Type, leaseValue.Type));
            Assert.Empty(owner.GetMembers("Value"));
            Assert.Empty(lease.GetMembers("DangerousValue"));
        }
    }

    /// <summary>
    /// Verifies that only the Win32 HANDLE gains SafeHandle inputs without type-level ownership annotations.
    /// </summary>
    /// <param name="allowMarshaling">Whether runtime marshaling is enabled.</param>
    /// <param name="useSafeHandles">Whether SafeHandle projection is enabled.</param>
    [Theory, CombinatorialData]
    public void HandleInputsWithoutTypeLevelOwnership(bool allowMarshaling, bool useSafeHandles)
    {
        this.GenerateFixture("RAIIFree", allowMarshaling, ["ConsumeHandle", "ConsumeOtherHandle", "ConsumeResource", "ConsumePointer"], useSafeHandles: useSafeHandles);
        this.compilation = this.AddCode($$"""
            using System.Runtime.InteropServices;
            using Windows.Win32;
            using Windows.Win32.Foundation;
            using Windows.Win32.Test;
            static unsafe class Usage
            {
                static void Use(SafeHandle owner, HANDLE handle, Windows.Win32.Other.HANDLE other, RESOURCE_A resource, void* pointer)
                {
                    PInvoke.ConsumeHandle({{(useSafeHandles ? "owner" : "handle")}});
                    PInvoke.ConsumeOtherHandle(other);
                    PInvoke.ConsumeResource(resource);
                    PInvoke.ConsumePointer(pointer);
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Equal(useSafeHandles, this.FindGeneratedMethod("ConsumeHandle").Any(method => method.ParameterList.Parameters[0].Type?.ToString() == "SafeHandle"));
        foreach (string name in new[] { "ConsumeOtherHandle", "ConsumeResource", "ConsumePointer" })
        {
            Assert.DoesNotContain(this.FindGeneratedMethod(name), method => method.ParameterList.Parameters[0].Type?.ToString() == "SafeHandle");
        }
    }

    /// <summary>
    /// Verifies the existing allocated-string annotation with a real native string consumer.
    /// </summary>
    [Fact]
    public async Task KnownFolderPathHasTypedOwnerAndLease()
    {
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("SHGetKnownFolderPath", CancellationToken.None));
        Assert.True(this.generator.TryGenerate("PathFileExists", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.compilation = this.AddCode("""
            using Windows.Win32;
            using Windows.Win32.Foundation;
            static class Usage
            {
                static bool Exists(CoTaskMemFreePWSTRSafeHandle path)
                {
                    using var lease = path.Lease();
                    return PInvoke.PathFileExists(lease.Value);
                }
                static void Get(System.Guid folder)
                {
                    PInvoke.SHGetKnownFolderPath(folder, 0, null, out CoTaskMemFreePWSTRSafeHandle path);
                    using (path)
                    {
                        PInvoke.PathFileExists(path.DangerousValue);
                    }
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(await this.AnalyzeAsync());
    }

    /// <summary>
    /// Verifies that interpolating an owned path into a string copies its contents within the lifetime scope.
    /// </summary>
    /// <param name="framework">The caller's target framework.</param>
    /// <param name="body">The caller's formatting code.</param>
    /// <param name="expectedDiagnostic">The expected diagnostic, or null for a supported pattern.</param>
    [Theory]
    [InlineData("net472", "using (path) { WriteLine($\"Path: {path.DangerousValue}\"); }", null)]
    [InlineData("net8.0", "using (path) { WriteLine($\"Path: {path.DangerousValue}\"); }", null)]
    [InlineData("net472", "using var lease = path.Lease(); WriteLine($\"Path: {lease.Value,20}\");", null)]
    [InlineData("net8.0", "using var lease = path.Lease(); WriteLine($\"Path: {lease.Value,20}\");", null)]
    [InlineData("net472", "string text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text);", null)]
    [InlineData("net8.0", "string text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text);", null)]
    [InlineData("net472", "WriteLine($\"Path: {path.DangerousValue}\");", "PInvoke016")]
    [InlineData("net8.0", "WriteLine($\"Path: {path.DangerousValue}\");", "PInvoke016")]
    [InlineData("net472", "System.FormattableString text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text.ToString());", "PInvoke017")]
    [InlineData("net8.0", "System.FormattableString text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text.ToString());", "PInvoke017")]
    [InlineData("net472", "System.IFormattable text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text.ToString(null, null));", "PInvoke017")]
    [InlineData("net8.0", "System.IFormattable text; using (path) { text = $\"Path: {path.DangerousValue}\"; } WriteLine(text.ToString(null, null));", "PInvoke017")]
    public async Task KnownFolderPathFormattingDiagnostics(string framework, string body, string? expectedDiagnostic)
    {
        this.compilation = this.starterCompilations[framework];
        this.generator = this.CreateGenerator();
        Assert.True(this.generator.TryGenerate("SHGetKnownFolderPath", CancellationToken.None));
        this.CollectGeneratedCode(this.generator);
        this.compilation = this.AddCode($$"""
            using Windows.Win32;
            static class Usage
            {
                static void Use()
                {
                    PInvoke.SHGetKnownFolderPath(default, 0, null, out var path);
                    {{body}}
                }
                static void WriteLine(string text) { }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        ImmutableArray<Diagnostic> diagnostics = await this.AnalyzeAsync();
        if (expectedDiagnostic is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Diagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(expectedDiagnostic, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }
    }

    /// <summary>
    /// Verifies that the lifetime contract is enforced with errors by default.
    /// </summary>
    [Fact]
    public void LifetimeDiagnosticsAreErrorsByDefault()
    {
        Assert.All(new SafeHandleAnalyzer().SupportedDiagnostics, descriptor =>
        {
            Assert.True(descriptor.IsEnabledByDefault);
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        });
    }

    /// <summary>
    /// Verifies that incompatible opaque handle cleanup retains the raw API rather than an unsafe owner.
    /// </summary>
    /// <param name="attribute">The cleanup attribute.</param>
    [Theory]
    [InlineData("RAIIFree")]
    [InlineData("FreeWith")]
    public void IncompatibleCleanupDoesNotProduceOwner(string attribute)
    {
        this.GenerateFixture(attribute, true, "IncorrectCleanup");
        Assert.All(this.FindGeneratedMethod("IncorrectCleanup"), method =>
            Assert.Equal("RESOURCE_A", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText));
        Assert.Empty(this.FindGeneratedMethod("IncorrectCleanup_SafeHandle"));
    }

    /// <summary>
    /// Verifies that the real incompatible recording-DC annotations leave the raw methods available.
    /// </summary>
    /// <param name="api">The recording DC factory.</param>
    [Theory]
    [InlineData("CreateMetaFile")]
    [InlineData("CreateEnhMetaFile")]
    public void MetafileRecordingContextsDoNotGetDeletionOwners(string api)
    {
        this.GenerateApi(api);
        Assert.All(this.FindGeneratedMethod(api), method =>
            Assert.Equal("HDC", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText));
        Assert.Empty(this.FindGeneratedMethod(api + "_SafeHandle"));
    }

    /// <summary>
    /// Verifies helpers with caller-supplied cleanup context without changing the factory to an abstract return type.
    /// </summary>
    [Fact]
    public async Task MultiParameterCleanupSupportsDerivedOwner()
    {
        this.GenerateFixture("RAIIFree", true, "CreateWithContext");
        this.compilation = this.AddCode("""
            using Windows.Win32;
            sealed class Owner : FreeContextSafeHandle
            {
                private readonly int context;
                internal Owner(System.IntPtr value, int context) : base(value) { this.context = context; }
                protected override bool ReleaseHandle() => PInvoke.FreeContext(this.DangerousValue, this.context);
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        Assert.Empty(await this.AnalyzeAsync());
        Assert.All(this.FindGeneratedMethod("CreateWithContext"), method =>
            Assert.Equal("RESOURCE_A", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText));
    }

    /// <summary>
    /// Verifies that a timer cleanup helper can be requested independently of a contextual factory annotation.
    /// </summary>
    [Fact]
    public void TimerQueueCleanupHelperCanBeRequested()
    {
        this.GenerateApi("DeleteTimerQueueTimerSafeHandle");
        var type = this.compilation.GetTypeByMetadataName("Windows.Win32.DeleteTimerQueueTimerSafeHandle");
        Assert.NotNull(type);
        Assert.True(type.IsAbstract);
        Assert.Equal("HANDLE", Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(type.GetMembers("DangerousValue"))).Type.Name);
    }

    /// <summary>
    /// Verifies that disabling SafeHandles still exposes ordinary native outputs.
    /// </summary>
    [Fact]
    public void SafeHandlesCanStillBeDisabled()
    {
        this.GenerateFixture("FreeWith", true, ["CreateResource", "AcquirePointer"], useSafeHandles: false);
        Assert.Empty(this.FindGeneratedType("FreeSharedSafeHandle"));
        Assert.All(this.FindGeneratedMethod("CreateResource"), method =>
            Assert.Equal("RESOURCE_A", Assert.IsType<QualifiedNameSyntax>(method.ReturnType).Right.Identifier.ValueText));
    }

    /// <summary>
    /// Verifies that same-named native types retain distinct ownership projections.
    /// </summary>
    [Fact]
    public void NativeNamespacesKeepOwnersDistinct()
    {
        this.GenerateFixture("RAIIFree", true, "CreateDuplicate", "CreateOtherDuplicate");
        this.compilation = this.AddCode("""
            using Windows.Win32;
            static class Usage
            {
                static void Use()
                {
                    using var a = PInvoke.CreateDuplicate_SafeHandle();
                    using var b = PInvoke.CreateOtherDuplicate_SafeHandle();
                    Windows.Win32.Test.DUPLICATE_RESOURCE valueA = a.DangerousValue;
                    Windows.Win32.Other.DUPLICATE_RESOURCE valueB = b.DangerousValue;
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }

    /// <summary>
    /// Verifies that a predeclared canonical owner need not support inheritance or the new typed helpers.
    /// </summary>
    [Fact]
    public void ExistingSealedOwnerRemainsUsable()
    {
        this.compilation = this.starterCompilations["net8.0"];
        this.compilation = this.AddCode("""
            namespace Windows.Win32
            {
                sealed class FreeSharedSafeHandle : global::System.Runtime.InteropServices.SafeHandle
                {
                    public FreeSharedSafeHandle() : base(global::System.IntPtr.Zero, true) { }
                    public FreeSharedSafeHandle(global::System.IntPtr value, bool ownsHandle = true) : base(global::System.IntPtr.Zero, ownsHandle) { this.SetHandle(value); }
                    public override bool IsInvalid => this.handle == global::System.IntPtr.Zero;
                    protected override bool ReleaseHandle() => true;
                }
            }
            """);
        this.GenerateFixture("RAIIFree", true, ["CreateResource"], useSafeHandles: true, preserveCompilation: true);
        this.compilation = this.AddCode("""
            using Windows.Win32;
            static class Usage
            {
                static void Use()
                {
                    using var owner = PInvoke.CreateResource_SafeHandle();
                    Windows.Win32.Test.RESOURCE_A value = owner.DangerousValue;
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }

    /// <summary>
    /// Verifies leases against older reference assemblies.
    /// </summary>
    /// <param name="framework">The caller's target framework.</param>
    [Theory]
    [InlineData("net35")]
    [InlineData("net472")]
    [InlineData("netstandard2.0")]
    public void LeasesCompileForSupportedFrameworks(string framework)
    {
        this.compilation = this.starterCompilations[framework];
        this.GenerateFixture("RAIIFree", true, ["CreateResource", "ConsumeResource"], useSafeHandles: true, preserveCompilation: true);
        this.compilation = this.AddCode("""
            using Windows.Win32;
            static class Usage
            {
                static void Use(FreeSharedRESOURCE_ASafeHandle owner)
                {
                    using var lease = owner.Lease();
                    PInvoke.ConsumeResource(lease.Value);
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }

    /// <summary>
    /// Verifies that generated leases do not change the nullability context of other generated members.
    /// </summary>
    [Fact]
    public void NullableEnabledProjectsCompile()
    {
        this.compilation = this.starterCompilations["net8.0"].WithOptions(
            this.starterCompilations["net8.0"].Options.WithNullableContextOptions(NullableContextOptions.Enable));
        this.GenerateFixture("RAIIFree", true, ["CreateResource", "CreateOtherResource"], useSafeHandles: true, preserveCompilation: true);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
    }

    /// <summary>
    /// Verifies accepted and rejected lifetime patterns through their diagnostics.
    /// </summary>
    /// <param name="body">The caller's code.</param>
    /// <param name="expectedDiagnostic">The expected diagnostic, or null for a supported pattern.</param>
    [Theory]
    [InlineData("using var lease = owner.Lease(); PInvoke.ConsumeResource(lease.Value);", null)]
    [InlineData("using (var lease = owner.Lease()) { PInvoke.ConsumeResource(lease.Value); }", null)]
    [InlineData("using (owner) { PInvoke.ConsumeResource(owner.DangerousValue); }", null)]
    [InlineData("using var local = owner; PInvoke.ConsumeResource(local.DangerousValue);", null)]
    [InlineData("using var lease = owner.Lease(); string text = lease.Value.ToString();", null)]
    [InlineData("using var lease = owner.Lease(); bool empty = lease.Value.IsNull;", null)]
    [InlineData("_ = nameof(owner.DangerousValue);", null)]
    [InlineData("using var lease = owner.Lease(); _ = nameof(lease.Value.Value);", null)]
    [InlineData("PInvoke.ConsumeResource(owner.DangerousValue);", "PInvoke016")]
    [InlineData("var lease = owner.Lease(); PInvoke.ConsumeResource(lease.Value);", "PInvoke015")]
    [InlineData("PInvoke.ConsumeResource(owner.Lease().Value);", "PInvoke015")]
    [InlineData("using var lease = owner.Lease(); var copy = lease;", "PInvoke015")]
    [InlineData("using var lease = owner.Lease(); lease.Dispose();", "PInvoke015")]
    [InlineData("using var lease = owner.Lease(); PassLease(lease);", "PInvoke015")]
    [InlineData("using var lease = owner.Lease(); var saved = lease.Value;", "PInvoke017")]
    [InlineData("using (owner) { var saved = owner.DangerousValue; }", "PInvoke017")]
    [InlineData("using (owner) { System.Action action = () => PInvoke.ConsumeResource(owner.DangerousValue); }", "PInvoke016")]
    [InlineData("using var lease = owner.Lease(); PInvoke.FreeShared(lease.Value.Value);", "PInvoke018")]
    [InlineData("using (owner) { PInvoke.FreeShared(owner.DangerousValue.Value); }", "PInvoke018")]
    public async Task LifetimeUsageDiagnostics(string body, string? expectedDiagnostic)
    {
        this.GenerateFixture("RAIIFree", true, "CreateResource", "ConsumeResource");
        this.compilation = this.AddCode($$"""
            using Windows.Win32;
            static unsafe class Usage
            {
                static void Use(FreeSharedRESOURCE_ASafeHandle owner) { {{body}} }
                static void PassLease(FreeSharedRESOURCE_ASafeHandle.LeaseScope lease) { }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        var diagnostics = await this.AnalyzeAsync();
        diagnostics = diagnostics.Where(diagnostic => diagnostic.Location.SourceSpan.Start < this.compilation.SyntaxTrees.Last().GetText().ToString().IndexOf("static void PassLease", StringComparison.Ordinal)).ToImmutableArray();
        if (expectedDiagnostic is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
            Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        }
    }

    /// <summary>
    /// Verifies that lifetime errors identify the owner's or lease's actual accessor.
    /// </summary>
    [Fact]
    public async Task LifetimeDiagnosticsIdentifyAccessor()
    {
        this.GenerateFixture("RAIIFree", true, "CreateResource", "ConsumeResource");
        this.compilation = this.AddCode("""
            using Windows.Win32;
            static unsafe class Usage
            {
                static void Use(FreeSharedRESOURCE_ASafeHandle owner)
                {
                    PInvoke.ConsumeResource(owner.DangerousValue);
                    using (owner)
                    {
                        var savedOwner = owner.DangerousValue;
                        PInvoke.FreeShared(owner.DangerousValue.Value);
                        using var lease = owner.Lease();
                        var savedLease = lease.Value;
                        PInvoke.FreeShared(lease.Value.Value);
                    }
                }
            }
            """);
        this.AssertNoDiagnostics(logAllGeneratedCode: false);
        var diagnostics = await this.AnalyzeAsync();
        Assert.Equal(
            [
                "Read DangerousValue inside the owner's using scope, or acquire a lease in a using local",
                "Use DangerousValue directly in a call or copy its contents; do not save or return the raw resource",
                "Do not pass DangerousValue to 'FreeShared'; dispose the SafeHandle instead",
                "Use Value directly in a call or copy its contents; do not save or return the raw resource",
                "Do not pass Value to 'FreeShared'; dispose the SafeHandle instead",
            ],
            diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).Select(diagnostic => diagnostic.GetMessage()).ToArray());
    }

    /// <summary>
    /// Verifies forbidden lease declarations even when the compiler permits them in ref structs or method signatures.
    /// </summary>
    /// <param name="declaration">The declaration that stores or transfers the lease.</param>
    [Theory]
    [InlineData("ref struct Storage { internal FreeSharedRESOURCE_ASafeHandle.LeaseScope Field; }")]
    [InlineData("static class Storage { static void Use(FreeSharedRESOURCE_ASafeHandle.LeaseScope lease) { } }")]
    [InlineData("static class Storage { static FreeSharedRESOURCE_ASafeHandle.LeaseScope Get() => default; }")]
    [InlineData("ref struct Storage { internal FreeSharedRESOURCE_ASafeHandle.LeaseScope Property { get; set; } }")]
    public async Task LeaseStorageAndTransfersAreDiagnosed(string declaration)
    {
        this.GenerateFixture("RAIIFree", true, "CreateResource");
        this.compilation = this.AddCode("using Windows.Win32;\n" + declaration);
        Assert.Contains(await this.AnalyzeAsync(), diagnostic => diagnostic.Id == "PInvoke015");
    }

    /// <summary>
    /// Observes reference balance, acquisition failures, failure outputs, and allocation counts.
    /// </summary>
    [Fact]
    public void LeaseAndFailureOutputRuntimeBehavior()
    {
        this.GenerateFixture("RAIIFree", true, "CreateResource", "AcquirePointer", "CreateUnsigned", "CreateUnsigned32");
        foreach (SyntaxTree tree in this.compilation.SyntaxTrees.ToArray())
        {
            var methods = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => method.Modifiers.Any(SyntaxKind.ExternKeyword) && method.Identifier.ValueText is "FreeShared" or "AcquirePointer" or "FreeUnsigned" or "FreeUnsigned32").ToArray();
            if (methods.Length == 0)
            {
                continue;
            }

            SyntaxNode rewritten = tree.GetRoot(TestContext.Current.CancellationToken).ReplaceNodes(methods, (original, current) =>
                current.WithAttributeLists(default)
                    .WithModifiers(TokenList(current.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ExternKeyword))))
                    .WithBody(ParseStatement(original.Identifier.ValueText switch
                    {
                        "FreeShared" => "{ TestBackend.Frees++; }",
                        "FreeUnsigned" => "{ if (resource.Value != unchecked((nuint)(nint)(-2))) throw new global::System.Exception(\"Lost unsigned bits during cleanup.\"); }",
                        "FreeUnsigned32" => "{ if (resource.Value != 0xfffffffeu) throw new global::System.Exception(\"Lost unsigned 32-bit value during cleanup.\"); }",
                        _ => "{ *resource = (void*)1; return -1; }",
                    }) as BlockSyntax)
                    .WithSemicolonToken(default));
            this.compilation = this.compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(rewritten.ToFullString(), this.parseOptions, tree.FilePath, cancellationToken: TestContext.Current.CancellationToken));
        }

        this.compilation = this.AddCode("""
            using System;
            using Windows.Win32;
            internal static class TestBackend { internal static int Frees; }
            /// <summary>Executes the generated owners and leases.</summary>
            public static unsafe class RuntimeUsage
            {
                /// <summary>Returns the allocation count for a warmed-up lease loop.</summary>
                public static long Run()
                {
                    TestBackend.Frees = 0;
                    var owner = new FreeSharedRESOURCE_ASafeHandle((IntPtr)1);
                    try
                    {
                        using var lease = owner.Lease();
                        owner.Dispose();
                        if (TestBackend.Frees != 0 || (nint)lease.Value.Value != 1) throw new Exception("Released while leased.");
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) { }
                    if (TestBackend.Frees != 1) throw new Exception("Unbalanced exceptional cleanup.");
                    bool rejected = false;
                    try { using var invalidLease = owner.Lease(); }
                    catch (ObjectDisposedException) { rejected = true; }
                    if (!rejected || TestBackend.Frees != 1) throw new Exception("Acquired a closed resource.");

                    int result = PInvoke.AcquirePointer(out FreeSharedSafeHandle allocation);
                    if (result != -1 || (nint)allocation.DangerousValue != 1) throw new Exception("Lost failure output.");
                    allocation.Dispose();
                    if (TestBackend.Frees != 2) throw new Exception("Did not release failure output.");

                    using (var unsigned = new FreeUnsignedSafeHandle((IntPtr)(-2)))
                    {
                        using var lease = unsigned.Lease();
                        if (lease.Value.Value != unchecked((nuint)(nint)(-2))) throw new Exception("Lost unsigned native value.");
                    }

                    using (var unsigned = new FreeUnsigned32SafeHandle(unchecked((IntPtr)(nint)(nuint)0xfffffffeu)))
                    {
                        using var lease = unsigned.Lease();
                        if (lease.Value.Value != 0xfffffffeu) throw new Exception("Lost unsigned 32-bit native value.");
                    }

                    using var measuredOwner = new FreeSharedRESOURCE_ASafeHandle((IntPtr)1);
                    for (int i = 0; i < 100; i++) { using var lease = measuredOwner.Lease(); }
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 1000; i++)
                    {
                        using var lease = measuredOwner.Lease();
                        if ((nint)lease.Value.Value != 1) throw new Exception("Wrong native value.");
                    }
                    if (TestBackend.Frees != 2) throw new Exception("Lease disposed the owner.");
                    return GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
            """);
        using var stream = new MemoryStream();
        var emitted = this.compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(0L, assembly.GetType("RuntimeUsage")!.GetMethod("Run")!.Invoke(null, null));
        Assert.Equal(3, assembly.GetType("TestBackend")!.GetField("Frees", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
    }

    private Task<ImmutableArray<Diagnostic>> AnalyzeAsync() => this.compilation.WithAnalyzers(
        ImmutableArray.Create<DiagnosticAnalyzer>(new SafeHandleAnalyzer())).GetAnalyzerDiagnosticsAsync();

    private void GenerateFixture(string attribute, bool allowMarshaling, params string[] apis) =>
        this.GenerateFixture(attribute, allowMarshaling, apis, useSafeHandles: true);

    private void GenerateFixture(string attribute, bool allowMarshaling, string[] apis, bool useSafeHandles, bool preserveCompilation = false)
    {
        if (!preserveCompilation)
        {
            this.compilation = this.starterCompilations["net8.0"];
        }

        CSharpCompilation metadata = CSharpCompilation.Create(
            "Windows.Win32",
            [CSharpSyntaxTree.ParseText(MetadataSource.Replace("__Cleanup", attribute, StringComparison.Ordinal), this.parseOptions)],
            this.starterCompilations["net8.0"].References,
            this.starterCompilations["net8.0"].Options);
        string metadataPath = Path.Combine(Path.GetTempPath(), $"CsWin32-Ownership-{Guid.NewGuid():N}.winmd");
        try
        {
            using (FileStream stream = File.Create(metadataPath))
            {
                var result = metadata.Emit(stream);
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            }

            this.generator = new Generator(metadataPath, null, [], DefaultTestGeneratorOptions with { AllowMarshaling = allowMarshaling, UseSafeHandles = useSafeHandles }, this.compilation, this.parseOptions);
            foreach (string api in apis)
            {
                Assert.True(this.generator.TryGenerate(api, CancellationToken.None));
            }

            this.CollectGeneratedCode(this.generator);
            this.AssertNoDiagnostics(logAllGeneratedCode: false);
        }
        finally
        {
            this.generator?.Dispose();
            this.generator = null;
            File.Delete(metadataPath);
        }
    }
}
