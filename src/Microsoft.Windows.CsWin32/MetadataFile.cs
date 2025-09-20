// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.MemoryMappedFiles;
using System.Reflection.PortableExecutable;

namespace Microsoft.Windows.CsWin32;

[DebuggerDisplay($"{{{nameof(DebuggerDisplay)},nq}}")]
internal class MetadataFile : IDisposable
{
    private readonly object syncObject = new();
    private readonly Stack<(PEReader PEReader, MetadataReader MDReader)> peReaders = new();
    private readonly Dictionary<Platform?, MetadataIndex> indexes = new();
    private int readersRentedOut;
    private MemoryMappedFile file;
    private bool obsolete;

    internal MetadataFile(string path)
    {
        this.Path = path;
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
        this.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers

        // When using FileShare.Delete, the OS will allow the file to be deleted, but it does not disrupt
        // our ability to read the file while our handle is open.
        // The file may be recreated on disk as well, and we'll keep reading the original file until we close that handle.
        // We may also open the new file while holding the old handle,
        // at which point we have handles open to both versions of the file concurrently.
        FileStream metadataStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        this.file = MemoryMappedFile.CreateFromFile(metadataStream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
    }

    internal string Path { get; }

    internal DateTime LastWriteTimeUtc { get; }

    private string DebuggerDisplay => $"\"{this.Path}\" ({this.LastWriteTimeUtc})";

    /// <summary>
    /// Prepares to close the file handle and release resources as soon as all rentals have been returned.
    /// </summary>
    public void Dispose()
    {
        lock (this.syncObject)
        {
            this.obsolete = true;

            // Drain our cache of readers (the ones that aren't currently being used).
            while (this.peReaders.Count > 0)
            {
                this.peReaders.Pop().PEReader.Dispose();
            }

            // Close the file if we have no readers rented out.
            if (this.readersRentedOut == 0)
            {
                this.file.Dispose();
            }
        }
    }

    internal Rental GetMetadataReader()
    {
        lock (this.syncObject)
        {
            if (this.obsolete)
            {
                throw new InvalidOperationException("This file was deleted and should no longer be used.");
            }

            PEReader peReader;
            MetadataReader metadataReader;
            if (this.peReaders.Count > 0)
            {
                (peReader, metadataReader) = this.peReaders.Pop();
            }
            else
            {
                peReader = new(this.file.CreateViewStream(offset: 0, size: 0, MemoryMappedFileAccess.Read));
                metadataReader = peReader.GetMetadataReader();
            }

            this.readersRentedOut++;
            return new Rental(peReader, metadataReader, this);
        }
    }

    internal MetadataIndex GetMetadataIndex(Platform? platform)
    {
        lock (this.syncObject)
        {
            if (!this.indexes.TryGetValue(platform, out MetadataIndex? index))
            {
                this.indexes.Add(platform, index = new MetadataIndex(this, platform));
            }

            return index;
        }
    }

    private void ReturnReader(PEReader peReader, MetadataReader mdReader)
    {
        lock (this.syncObject)
        {
            this.readersRentedOut--;
            Debug.Assert(this.readersRentedOut >= 0, "Some reader was returned more than once.");

            if (this.obsolete)
            {
                // This file has been marked as stale, so we don't want to recycle the reader.
                peReader.Dispose();

                // If this was the last rental to be returned, we can close the file.
                if (this.readersRentedOut == 0)
                {
                    this.file.Dispose();
                }
            }
            else
            {
                // Store this in the cache for reuse later.
                this.peReaders.Push((peReader, mdReader));
            }
        }
    }

    internal class Rental : IDisposable
    {
        private (PEReader PEReader, MetadataReader MDReader, MetadataFile File)? state;

        internal Rental(PEReader peReader, MetadataReader mdReader, MetadataFile file)
        {
            this.state = (peReader, mdReader, file);
        }

        internal MetadataReader Value => this.state?.MDReader ?? throw new ObjectDisposedException(typeof(Rental).FullName);

        public void Dispose()
        {
            if (this.state is (PEReader peReader, MetadataReader mdReader, MetadataFile file))
            {
                file.ReturnReader(peReader, mdReader);
                this.state = null;
            }
        }
    }
}
