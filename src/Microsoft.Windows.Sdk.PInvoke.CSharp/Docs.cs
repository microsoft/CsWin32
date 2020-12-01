// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.Sdk.PInvoke.CSharp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Reflection;

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
            using Stream? docsYamlStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ThisAssembly.RootNamespace + ".apidocs.yml");
            if (docsYamlStream is null)
            {
                ////return new Docs(new Dictionary<string, ApiDetails>());
                throw new Exception("YAML documentation not found.");
            }

            using var yamlTextReader = new StreamReader(docsYamlStream);
            var deserializer = new YamlDotNet.Serialization.Deserializer();
            var data = deserializer.Deserialize<Dictionary<string, ApiDetails>>(yamlTextReader);

            return new Docs(data);
        }

#pragma warning disable CA1812 // uninstantiated class is deserialized into
        internal class ApiDetails
#pragma warning restore CA1812 // uninstantiated class is deserialized into
        {
            public Uri? HelpLink { get; set; }

            public string? Description { get; set; }

            public string? Remarks { get; set; }

            public Dictionary<string, string>? Parameters { get; set; }

            public Dictionary<string, string>? Fields { get; set; }

            public string? ReturnValue { get; set; }
        }
    }
}
