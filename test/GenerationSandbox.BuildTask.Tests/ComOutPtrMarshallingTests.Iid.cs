// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Graphics.Imaging;
using Windows.Win32;
using WinRT;

namespace GenerationSandbox.BuildTask.Tests;

public partial class ComOutPtrMarshallingTests
{
    /// <summary>
    /// Verifies that a runtime class uses its default interface IID rather than its class signature.
    /// </summary>
    [Fact]
    public void RuntimeClassIid_UsesDefaultInterface()
    {
        Assert.Equal(
            new Guid("689E0708-7EEF-483F-963F-DA938818E073"),
            ComOrWinRTObjectMarshaller.GetIID<SoftwareBitmap>());
    }

    /// <summary>
    /// Verifies that parameterized default interfaces retain their constructed interface IID.
    /// </summary>
    [Fact]
    public void RuntimeClassIid_SupportsParameterizedDefaultInterface()
    {
        Assert.Equal(
            ComOrWinRTObjectMarshaller.GetIID<IReadOnlyList<string>>(),
            ComOrWinRTObjectMarshaller.GetIID<ProjectedList>());
    }

    /// <summary>
    /// Verifies that legacy property-name-based projections are rejected without a reflection fallback.
    /// </summary>
    [Fact]
    public void RuntimeClassIid_RejectsLegacyProjection()
    {
        Assert.Throws<NotSupportedException>(() => ComOrWinRTObjectMarshaller.GetIID<LegacyProjection>());
    }

    [WindowsRuntimeType]
    [ProjectedRuntimeClass(typeof(IReadOnlyList<string>))]
    private sealed class ProjectedList;

    [WindowsRuntimeType]
    [ProjectedRuntimeClass("DefaultInterface")]
    private sealed class LegacyProjection;
}
