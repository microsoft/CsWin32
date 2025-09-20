// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// An in-memory representation of API documentation.
/// </summary>
public class Docs
{
    private static readonly Dictionary<string, Docs> DocsByPath = new Dictionary<string, Docs>(StringComparer.OrdinalIgnoreCase);
    private static readonly MessagePackSerializerOptions MsgPackOptions = MessagePackSerializerOptions.Standard.WithResolver(
        CompositeResolver.Create(
            new IMessagePackFormatter[] { new ApiDetailsFormatter() },
            new IFormatterResolver[] { StandardResolver.Instance }));

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
        Dictionary<string, ApiDetails>? data = MessagePackSerializer.Deserialize<Dictionary<string, ApiDetails>>(docsStream, MsgPackOptions);
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

    /// <summary>
    /// Formatter for <see cref="ApiDetails"/>.
    /// </summary>
    /// <remarks>
    /// We have to manually write this to avoid using the <see cref="DynamicObjectResolver"/> since newer C# compiler versions fail
    /// when that dynamic type creator creates a non-collectible assembly that our own evidently collectible assembly references.
    /// </remarks>
    private class ApiDetailsFormatter : IMessagePackFormatter<ApiDetails>
    {
        public ApiDetails Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            string? helpLink = null;
            string? description = null;
            string? remarks = null;
            Dictionary<string, string>? parameters = null;
            Dictionary<string, string>? fields = null;
            string? returnValue = null;
            int count = reader.ReadArrayHeader();
            for (int i = 0; i < count; i++)
            {
                switch (i)
                {
                    case 0:
                        helpLink = reader.ReadString();
                        break;
                    case 1:
                        description = reader.ReadString();
                        break;
                    case 2:
                        remarks = reader.ReadString();
                        break;
                    case 3:
                        parameters = options.Resolver.GetFormatterWithVerify<Dictionary<string, string>>().Deserialize(ref reader, options);
                        break;
                    case 4:
                        fields = options.Resolver.GetFormatterWithVerify<Dictionary<string, string>>().Deserialize(ref reader, options);
                        break;
                    case 5:
                        returnValue = reader.ReadString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new ApiDetails
            {
                HelpLink = helpLink is null ? null : new Uri(helpLink),
                Description = description,
                Remarks = remarks,
                Parameters = parameters ?? new Dictionary<string, string>(),
                Fields = fields ?? new Dictionary<string, string>(),
                ReturnValue = returnValue,
            };
        }

        public void Serialize(ref MessagePackWriter writer, ApiDetails value, MessagePackSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
