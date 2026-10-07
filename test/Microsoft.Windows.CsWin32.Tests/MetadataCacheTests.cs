// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32.Tests;

/// <summary>
/// Tests the lifetime of cached metadata files and their readers.
/// </summary>
public class MetadataCacheTests
{
    private static readonly string MetadataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Windows.Win32.winmd");

    /// <summary>
    /// Verifies that eviction closes returned readers and allows the same path to be reopened.
    /// </summary>
    [Fact]
    public void RemoveMetadataFileClosesCachedReaders()
    {
        MetadataCache cache = new();
        try
        {
            MetadataFile original = cache.GetMetadataFile(MetadataPath);
            using (MetadataFile.Rental reader = original.GetMetadataReader())
            {
                Assert.NotNull(reader.Value);
            }

            cache.RemoveMetadataFile(MetadataPath);
            Assert.Throws<InvalidOperationException>(() => original.GetMetadataReader());

            MetadataFile reopened = cache.GetMetadataFile(MetadataPath);
            Assert.NotSame(original, reopened);
            using MetadataFile.Rental reopenedReader = reopened.GetMetadataReader();
            Assert.NotNull(reopenedReader.Value);
        }
        finally
        {
            cache.RemoveMetadataFile(MetadataPath);
        }
    }

    /// <summary>
    /// Verifies that eviction does not invalidate a reader that has not yet been returned.
    /// </summary>
    [Fact]
    public void RemoveMetadataFilePreservesActiveReaders()
    {
        MetadataCache cache = new();
        try
        {
            MetadataFile original = cache.GetMetadataFile(MetadataPath);
            using MetadataFile.Rental reader = original.GetMetadataReader();
            MetadataReader metadata = reader.Value;
            string assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);

            cache.RemoveMetadataFile(MetadataPath);

            Assert.Equal(assemblyName, metadata.GetString(metadata.GetAssemblyDefinition().Name));
            Assert.Throws<InvalidOperationException>(() => original.GetMetadataReader());
            Assert.NotSame(original, cache.GetMetadataFile(MetadataPath));
        }
        finally
        {
            cache.RemoveMetadataFile(MetadataPath);
        }
    }
}
