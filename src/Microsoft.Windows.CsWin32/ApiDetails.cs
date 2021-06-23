// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ScrapeDocs
{
    using System;
    using System.Collections.Generic;
    using MessagePack;

    /// <summary>
    /// Captures all the documentation we have available for an API.
    /// </summary>
    [MessagePackObject]
    public class ApiDetails
    {
        /// <summary>
        /// Gets or sets the URL that provides more complete documentation for this API.
        /// </summary>
        [Key(0)]
        public Uri? HelpLink { get; set; }

        /// <summary>
        /// Gets or sets a summary of what the API is for.
        /// </summary>
        [Key(1)]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the remarks section of the documentation.
        /// </summary>
        [Key(2)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Gets a collection of parameter docs, keyed by their names.
        /// </summary>
        [Key(3)]
        public Dictionary<string, string> Parameters { get; } = new();

        /// <summary>
        /// Gets a collection of field docs, keyed by their names.
        /// </summary>
        [Key(4)]
        public Dictionary<string, string> Fields { get; } = new();

        /// <summary>
        /// Gets or sets the documentation of the return value of the API, if applicable.
        /// </summary>
        [Key(5)]
        public string? ReturnValue { get; set; }
    }
}
