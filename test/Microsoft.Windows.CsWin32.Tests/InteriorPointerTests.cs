// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Tests projections of native buffers that contain borrowed interior pointers.
/// </summary>
public class InteriorPointerTests : GeneratorTestBase
{
    private const string MetadataSource = """
        using System;
        using System.Runtime.InteropServices;
        using Windows.Win32.Foundation.Metadata;

        namespace Windows.Win32.Foundation.Metadata
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class ContainsInteriorPointersAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class MemorySizeAttribute : Attribute
            {
                public short BytesParamIndex { get; set; }
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeArrayInfoAttribute : Attribute
            {
                public short CountParamIndex { get; set; }
                public int CountConst { get; set; }
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class ConstAttribute : Attribute { }
        }

        namespace Windows.Win32.Test
        {
            public unsafe struct RECORD
            {
                public byte* Value;
            }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            public delegate void CALLBACK();

            public static unsafe class Apis
            {
                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void ByteBuffer(
                    [ContainsInteriorPointers] [Out] byte* buffer, uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void MemorySizedByteBuffer(
                    [ContainsInteriorPointers] [Out, MemorySize(BytesParamIndex = 1)] byte* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void VoidBuffer(
                    [ContainsInteriorPointers] [Out, MemorySize(BytesParamIndex = 1)] void* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void RecordBuffer(
                    [ContainsInteriorPointers] [Out, MemorySize(BytesParamIndex = 1)] RECORD* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void CountedRecordBuffer(
                    [ContainsInteriorPointers] [Out, NativeArrayInfo(CountParamIndex = 1)] RECORD* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void ConstantRecordBuffer(
                    [ContainsInteriorPointers] [Out, NativeArrayInfo(CountConst = 1)] RECORD* buffer,
                    [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void OptionalRecordBuffer(
                    [ContainsInteriorPointers] [Out, Optional, MemorySize(BytesParamIndex = 1)] RECORD* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void ConstRecordBuffer(
                    [ContainsInteriorPointers] [In, Const, MemorySize(BytesParamIndex = 1)] RECORD* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void SingleRecord(
                    [ContainsInteriorPointers] [Out] RECORD* buffer, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void ScalarBuffer(
                    [ContainsInteriorPointers] [Out] uint* buffer, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void CallbackBuffer(
                    [ContainsInteriorPointers] [Out, NativeArrayInfo(CountParamIndex = 1)] CALLBACK* buffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void MixedBuffers(
                    [ContainsInteriorPointers] [Out, MemorySize(BytesParamIndex = 1)] void* buffer,
                    uint capacity, [Out, MemorySize(BytesParamIndex = 3)] byte* ordinaryBuffer,
                    uint ordinaryCapacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void SharedCapacity(
                    [ContainsInteriorPointers] [Out, MemorySize(BytesParamIndex = 2)] byte* buffer,
                    [Out, MemorySize(BytesParamIndex = 2)] byte* ordinaryBuffer,
                    uint capacity, [Out] uint* written);

                [DllImport("kernel32.dll", ExactSpelling = true)]
                public static extern void SharedCapacityReversed(
                    [Out, NativeArrayInfo(CountParamIndex = 2)] RECORD* ordinaryBuffer,
                    [ContainsInteriorPointers] [Out, NativeArrayInfo(CountParamIndex = 2)] RECORD* buffer,
                    uint capacity, [Out] uint* written);
            }
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteriorPointerTests"/> class.
    /// </summary>
    /// <param name="logger">The test output logger.</param>
    public InteriorPointerTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies that annotated buffers retain native pointer types and explicit capacities in every overload.
    /// </summary>
    /// <param name="api">The buffer shape to generate.</param>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    /// <param name="includePointerOverloads">Whether additional pointer overloads are requested.</param>
    [Theory, CombinatorialData]
    public void AnnotatedBuffersRemainPointers(
        [CombinatorialValues("ByteBuffer", "MemorySizedByteBuffer", "VoidBuffer", "RecordBuffer", "CountedRecordBuffer", "ConstantRecordBuffer", "OptionalRecordBuffer", "ConstRecordBuffer", "SingleRecord", "ScalarBuffer", "CallbackBuffer")] string api,
        bool allowMarshaling,
        bool includePointerOverloads)
    {
        this.GenerateFromMetadata(api, containsInteriorPointers: true, allowMarshaling, includePointerOverloads);

        MethodDeclarationSyntax[] methods = this.FindGeneratedMethod(api).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            ParameterSyntax buffer = Assert.Single(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == "buffer");
            Assert.IsType<PointerTypeSyntax>(buffer.Type);
            Assert.Empty(buffer.Modifiers);
            Assert.Equal(api is not ("ConstantRecordBuffer" or "SingleRecord" or "ScalarBuffer"), method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "capacity"));
        });

        Assert.Contains(methods, method => !IsOrContainsExternMethod(method)
            && method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "written" && parameter.Modifiers.Any(SyntaxKind.OutKeyword)));
    }

    /// <summary>
    /// Verifies that ordinary buffer projections remain available when the annotation is absent.
    /// </summary>
    /// <param name="api">The buffer shape to generate.</param>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    [Theory, CombinatorialData]
    public void UnannotatedBuffersStillUseSpans(
        [CombinatorialValues("ByteBuffer", "MemorySizedByteBuffer", "VoidBuffer", "RecordBuffer", "CountedRecordBuffer", "ConstantRecordBuffer", "OptionalRecordBuffer", "ConstRecordBuffer")] string api,
        bool allowMarshaling)
    {
        this.GenerateFromMetadata(api, containsInteriorPointers: false, allowMarshaling, includePointerOverloads: false);
        Assert.Contains(this.FindGeneratedMethod(api), method => method.ParameterList.Parameters.Any(parameter =>
            parameter.Identifier.ValueText == "buffer"
            && parameter.Type is GenericNameSyntax { Identifier.ValueText: "Span" or "ReadOnlySpan" }));
    }

    /// <summary>
    /// Verifies that unrelated buffers and scalar outputs still receive friendly projections.
    /// </summary>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    /// <param name="includePointerOverloads">Whether additional pointer overloads are requested.</param>
    [Theory, CombinatorialData]
    public void OtherParametersRetainFriendlyProjections(bool allowMarshaling, bool includePointerOverloads)
    {
        const string Api = "MixedBuffers";
        this.GenerateFromMetadata(Api, containsInteriorPointers: true, allowMarshaling, includePointerOverloads);

        Assert.All(this.FindGeneratedMethod(Api), method =>
        {
            Assert.IsType<PointerTypeSyntax>(Assert.Single(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == "buffer").Type);
            Assert.Contains(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == "capacity");
        });
        Assert.Contains(this.FindGeneratedMethod(Api), method =>
            method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "ordinaryBuffer" && parameter.Type is GenericNameSyntax { Identifier.ValueText: "Span" })
            && !method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "ordinaryCapacity"));
    }

    /// <summary>
    /// Verifies that other buffers cannot remove a capacity needed by an annotated pointer buffer.
    /// </summary>
    /// <param name="api">The order and size annotation to generate.</param>
    /// <param name="allowMarshaling">Whether to enable marshaling.</param>
    [Theory, CombinatorialData]
    public void SharedCapacitiesRemainExplicit(
        [CombinatorialValues("SharedCapacity", "SharedCapacityReversed")] string api,
        bool allowMarshaling)
    {
        this.GenerateFromMetadata(api, containsInteriorPointers: true, allowMarshaling, includePointerOverloads: false);
        Assert.All(this.FindGeneratedMethod(api), method =>
        {
            Assert.Contains(method.ParameterList.Parameters, parameter => parameter.Identifier.ValueText == "capacity");
            Assert.All(method.ParameterList.Parameters.Where(parameter => parameter.Identifier.ValueText is "buffer" or "ordinaryBuffer"), parameter => Assert.IsType<PointerTypeSyntax>(parameter.Type));
        });
    }

    private void GenerateFromMetadata(string api, bool containsInteriorPointers, bool allowMarshaling, bool includePointerOverloads)
    {
        this.compilation = this.starterCompilations["net8.0"];
        string source = containsInteriorPointers ? MetadataSource : MetadataSource.Replace("[ContainsInteriorPointers]", string.Empty, StringComparison.Ordinal);
        CSharpCompilation metadataCompilation = CSharpCompilation.Create(
            "Windows.Win32",
            [CSharpSyntaxTree.ParseText(source, this.parseOptions)],
            this.compilation.References,
            this.compilation.Options);
        string metadataPath = Path.Combine(Path.GetTempPath(), $"CsWin32-InteriorPointers-{Guid.NewGuid():N}.winmd");
        try
        {
            using (FileStream stream = File.Create(metadataPath))
            {
                var result = metadataCompilation.Emit(stream);
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            }

            GeneratorOptions options = DefaultTestGeneratorOptions with
            {
                AllowMarshaling = allowMarshaling,
                FriendlyOverloads = new() { IncludePointerOverloads = includePointerOverloads },
            };
            this.generator = new Generator(metadataPath, null, [], options, this.compilation, this.parseOptions);
            this.GenerateApi(api);
        }
        finally
        {
            this.generator?.Dispose();
            this.generator = null;
            File.Delete(metadataPath);
        }
    }
}
