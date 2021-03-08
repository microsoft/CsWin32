// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ScrapeDocs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal class DocEnum
    {
        internal DocEnum(bool isFlags, IReadOnlyDictionary<string, (ulong? Value, string? Doc)> members)
        {
            this.IsFlags = isFlags;
            this.Members = members;
        }

        internal bool IsFlags { get; }

        internal IReadOnlyDictionary<string, (ulong? Value, string? Doc)> Members { get; }

        public override bool Equals(object? obj) => this.Equals(obj as DocEnum);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = this.IsFlags ? 1 : 0;
                foreach (KeyValuePair<string, (ulong? Value, string? Doc)> entry in this.Members)
                {
                    hash += entry.Key.GetHashCode();
                    hash += (int)(entry.Value.Value ?? 0u);
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

            if (this.Members.Count != other.Members.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, (ulong? Value, string? Doc)> entry in this.Members)
            {
                if (!other.Members.TryGetValue(entry.Key, out (ulong? Value, string? Doc) value))
                {
                    return false;
                }

                if (entry.Value.Value != value.Value)
                {
                    return false;
                }
            }

            return true;
        }

        internal string? GetRecommendedName(List<(string MethodName, string ParameterName, string HelpLink, bool IsMethod)> uses)
        {
            string? enumName = null;
            if (uses.Count == 1)
            {
                var oneValue = uses[0];
                if (oneValue.ParameterName.Contains("flags", StringComparison.OrdinalIgnoreCase))
                {
                    // Only appears in one method, on a parameter named something like "flags".
                    enumName = $"{oneValue.MethodName}Flags";
                }
                else
                {
                    enumName = $"{oneValue.MethodName}_{oneValue.ParameterName}Flags";
                }
            }
            else
            {
                string firstName = this.Members.Keys.First();
                int commonPrefixLength = firstName.Length;
                foreach (string key in this.Members.Keys)
                {
                    commonPrefixLength = Math.Min(commonPrefixLength, GetCommonPrefixLength(key, firstName));
                }

                if (commonPrefixLength > 1)
                {
                    int last_ = firstName.LastIndexOf('_', commonPrefixLength - 1);
                    if (last_ != -1 && last_ != commonPrefixLength - 1)
                    {
                        // Trim down to last underscore
                        commonPrefixLength = last_;
                    }

                    if (commonPrefixLength > 1 && firstName[commonPrefixLength - 1] == '_')
                    {
                        // The enum values share a common prefix suitable to imply a name for the enum.
                        enumName = firstName.Substring(0, commonPrefixLength - 1);
                    }
                }
            }

            return enumName;
        }

        private static int GetCommonPrefixLength(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        {
            int count = 0;
            int minLength = Math.Min(first.Length, second.Length);
            for (int i = 0; i < minLength; i++)
            {
                if (first[i] == second[i])
                {
                    count++;
                }
                else
                {
                    break;
                }
            }

            return count;
        }
    }
}
