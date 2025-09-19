// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Windows.CsWin32;
using Nerdbank.MessagePack;
using PolyType;

[assembly: TypeShapeExtension(typeof(ApiDetails), Marshaler = typeof(Docs.ApiDetailsMarshaler))]

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// An in-memory representation of API documentation.
/// </summary>
public partial class Docs
{
    private static readonly Dictionary<string, Docs> DocsByPath = new Dictionary<string, Docs>(StringComparer.OrdinalIgnoreCase);
    private static readonly MessagePackSerializer Serializer = new();

    private readonly Dictionary<string, ApiDetails> apisAndDocs;

    private Docs(Dictionary<string, ApiDetails> apisAndDocs)
    {
        this.apisAndDocs = apisAndDocs;
    }

    /// <summary>
    /// Loads docs from a file.
    /// </summary>
    /// <param name="docsPath">The messagepack docs file to read from.</param>
    /// <returns>An instance of <see cref="Docs"/> that accesses the documentation in the file specified by <paramref name="docsPath"/>.</returns>
    public static Docs Get(string docsPath)
    {
        lock (DocsByPath)
        {
            if (DocsByPath.TryGetValue(docsPath, out Docs? existing))
            {
                return existing;
            }
        }

#pragma warning disable RS1035 // Do not use APIs banned for analyzers
        using FileStream docsStream = File.OpenRead(docsPath);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
        Dictionary<string, ApiDetails> data = Serializer.Deserialize<Dictionary<string, ApiDetails>>(docsStream, Witness.GeneratedTypeShapeProvider) ?? throw new InvalidOperationException("Unable to read API docs.");
        var docs = new Docs(data);

        lock (DocsByPath)
        {
            if (DocsByPath.TryGetValue(docsPath, out Docs? existing))
            {
                return existing;
            }

            DocsByPath.Add(docsPath, docs);
            return docs;
        }
    }

    /// <summary>
    /// Returns a <see cref="Docs"/> instance that contains all the merged documentation from a list of docs.
    /// </summary>
    /// <param name="docs">The docs to be merged. When API documentation is provided by multiple docs in this list, the first one appearing in this list is taken.</param>
    /// <returns>An instance that contains all the docs provided. When <paramref name="docs"/> contains exactly one element, that element is returned.</returns>
    public static Docs Merge(IReadOnlyList<Docs> docs)
    {
        if (docs is null)
        {
            throw new ArgumentNullException(nameof(docs));
        }

        if (docs.Count == 1)
        {
            // Nothing to merge.
            return docs[0];
        }

        Dictionary<string, ApiDetails> mergedDocs = new(docs.Sum(d => d.apisAndDocs.Count), StringComparer.OrdinalIgnoreCase);
        foreach (Docs doc in docs)
        {
            foreach (KeyValuePair<string, ApiDetails> api in doc.apisAndDocs)
            {
                // We want a first one wins policy.
                if (!mergedDocs.ContainsKey(api.Key))
                {
                    mergedDocs.Add(api.Key, api.Value);
                }
            }
        }

        return new Docs(mergedDocs);
    }

    internal bool TryGetApiDocs(string apiName, [NotNullWhen(true)] out ApiDetails? docs) => this.apisAndDocs.TryGetValue(apiName, out docs);

    internal class ApiDetailsMarshaler : IMarshaler<ApiDetails, ApiDetailsMarshaler.Surrogate>
    {
        public Surrogate? Marshal(ApiDetails? value) => throw new NotImplementedException();

        public ApiDetails? Unmarshal(Surrogate? value)
        {
            if (value is null)
            {
                return null;
            }

            return new ApiDetails
            {
                HelpLink = value.HelpLink,
                Description = value.Description,
                Remarks = value.Remarks,
                Parameters = value.Parameters,
                Fields = value.Fields,
                ReturnValue = value.ReturnValue,
            };
        }

        public class Surrogate
        {
            [Key(0)]
            public Uri? HelpLink { get; set; }

            [Key(1)]
            public string? Description { get; set; }

            [Key(2)]
            public string? Remarks { get; set; }

            [Key(3)]
            public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

            [Key(4)]
            public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

            [Key(5)]
            public string? ReturnValue { get; set; }
        }
    }

    [GenerateShapeFor<Dictionary<string, ApiDetails>>]
    private partial class Witness;
}
