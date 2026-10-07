// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

internal class MetadataCache
{
    internal static readonly MetadataCache Default = new();

    private readonly Dictionary<string, MetadataFile> metadataFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a file accessor for the given path that supports many concurrent readers.
    /// </summary>
    /// <param name="path">The path to the .winmd file.</param>
    /// <returns>The file accessor.</returns>
    internal MetadataFile GetMetadataFile(string path)
    {
        lock (this.metadataFiles)
        {
            MetadataFile? metadataFile;
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
            if (this.metadataFiles.TryGetValue(path, out metadataFile))
            {
                if (metadataFile.LastWriteTimeUtc == lastWriteTimeUtc)
                {
                    // We already have the file, and it is still current. Happy path.
                    return metadataFile;
                }

                // Stale file. Evict from the cache.
                this.metadataFiles.Remove(path);
                metadataFile.Dispose();
            }

            // New or updated file. Re-open.
            this.metadataFiles.Add(path, metadataFile = new MetadataFile(path));
            return metadataFile;
        }
    }
}
