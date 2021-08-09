// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Reflection;
    using MessagePack;
    using Microsoft.Windows.SDK.Win32Docs;

    internal class Docs
    {
        private static readonly Dictionary<string, Docs> DocsByPath = new Dictionary<string, Docs>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ApiDetails> apisAndDocs;

        private Docs(Dictionary<string, ApiDetails> apisAndDocs)
        {
            this.apisAndDocs = apisAndDocs;
        }

        internal static Docs Get(string docsPath)
        {
            lock (DocsByPath)
            {
                if (DocsByPath.TryGetValue(docsPath, out Docs? existing))
                {
                    return existing;
                }
            }

            using FileStream docsStream = File.OpenRead(docsPath);
            var data = MessagePackSerializer.Deserialize<Dictionary<string, ApiDetails>>(docsStream);
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

        internal bool TryGetApiDocs(string apiName, [NotNullWhen(true)] out ApiDetails? docs) => this.apisAndDocs.TryGetValue(apiName, out docs);
    }
}
