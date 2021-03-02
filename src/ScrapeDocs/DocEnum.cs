// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ScrapeDocs
{
    using System.Collections.Generic;

    internal class DocEnum
    {
        internal DocEnum(bool isFlags, IReadOnlyDictionary<string, string?> memberNamesAndDocs)
        {
            this.IsFlags = isFlags;
            this.MemberNamesAndDocs = memberNamesAndDocs;
        }

        internal bool IsFlags { get; }

        internal IReadOnlyDictionary<string, string?> MemberNamesAndDocs { get; }

        public override bool Equals(object? obj) => this.Equals(obj as DocEnum);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = this.IsFlags ? 1 : 0;
                foreach (KeyValuePair<string, string?> entry in this.MemberNamesAndDocs)
                {
                    hash += entry.Key.GetHashCode();
                    hash += entry.Value?.GetHashCode() ?? 0;
                }

                return hash;
            }
        }

        public bool Equals(DocEnum? other)
        {
            if (other is null)
            {
                return false;
            }

            if (this.IsFlags != other.IsFlags)
            {
                return false;
            }

            if (this.MemberNamesAndDocs.Count != other.MemberNamesAndDocs.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string?> entry in this.MemberNamesAndDocs)
            {
                if (!other.MemberNamesAndDocs.TryGetValue(entry.Key, out string? value))
                {
                    return false;
                }

                if (entry.Value != value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
