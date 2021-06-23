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
    using ScrapeDocs;

    internal class Docs
    {
        private readonly Dictionary<string, ApiDetails> apisAndDocs;

        private Docs(Dictionary<string, ApiDetails> apisAndDocs)
        {
            this.apisAndDocs = apisAndDocs;
        }

        internal static Docs Instance { get; } = Create();

        internal bool TryGetApiDocs(string apiName, [NotNullWhen(true)] out ApiDetails? docs) => this.apisAndDocs.TryGetValue(apiName, out docs);

        private static Docs Create()
        {
            using Stream? docsStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ThisAssembly.RootNamespace + ".apidocs.msgpack");
            if (docsStream is null)
            {
                ////return new Docs(new Dictionary<string, ApiDetails>());
                throw new Exception("Documentation not found.");
            }

            var data = MessagePackSerializer.Deserialize<Dictionary<string, ApiDetails>>(docsStream);
            return new Docs(data);
        }
    }
}
