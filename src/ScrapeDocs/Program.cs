// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ScrapeDocs
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using YamlDotNet.RepresentationModel;

    /// <summary>
    /// Program entrypoint class.
    /// </summary>
    internal class Program
    {
        private static readonly Regex FileNamePattern = new Regex(@"^\w\w-\w+-([\w\-]+)$", RegexOptions.Compiled);
        private static readonly Regex ParameterHeaderPattern = new Regex(@"^### -param (\w+)", RegexOptions.Compiled);
        private static readonly Regex FieldHeaderPattern = new Regex(@"^### -field (?:\w+\.)*(\w+)", RegexOptions.Compiled);
        private static readonly Regex ReturnHeaderPattern = new Regex(@"^## -returns", RegexOptions.Compiled);
        private static readonly Regex RemarksHeaderPattern = new Regex(@"^## -remarks", RegexOptions.Compiled);
        private static readonly Regex InlineCodeTag = new Regex(@"\<code\>(.*)\</code\>", RegexOptions.Compiled);
        private static readonly Regex EnumNameCell = new Regex(@"\<td[^\>]*\>\<a id=""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex EnumOrdinalValue = new Regex(@"\<dt\>([\dxa-f]+)\<\/dt\>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly string contentBasePath;
        private readonly string outputPath;

        private Program(string contentBasePath, string outputPath)
        {
            this.contentBasePath = contentBasePath;
            this.outputPath = outputPath;
        }

        private bool EmitEnums { get; set; }

        private static int Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Canceling...");
                cts.Cancel();
                e.Cancel = true;
            };

            if (args.Length < 2)
            {
                Console.Error.WriteLine("USAGE: {0} <path-to-docs> <path-to-output-yml> [enums]");
                return 1;
            }

            string contentBasePath = args[0];
            string outputPath = args[1];
            bool emitEnums = args.Length > 2 ? args[2] == "enums" : false;

            try
            {
                new Program(contentBasePath, outputPath) { EmitEnums = true }.Worker(cts.Token);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
            {
                return 2;
            }

            return 0;
        }

        private static void Expect(string? expected, string? actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected: \"{expected}\" but read: \"{actual}\".");
            }
        }

        // Skip the NULL constant due to https://github.com/aaubry/YamlDotNet/issues/591.
        private static bool IsYamlProblematicKey(string key) => string.Equals(key, "null", StringComparison.OrdinalIgnoreCase);

        private int AnalyzeEnums(ConcurrentDictionary<YamlNode, YamlNode> results, ConcurrentDictionary<(string MethodName, string ParameterName, string HelpLink), DocEnum> parameterEnums, ConcurrentDictionary<(string MethodName, string ParameterName, string HelpLink), DocEnum> fieldEnums)
        {
            var uniqueEnums = new Dictionary<DocEnum, List<(string MethodOrStructName, string ParameterOrFieldName, string HelpLink, bool IsMethod)>>();
            var constantsDocs = new Dictionary<string, List<(string MethodOrStructName, string HelpLink, string Doc)>>();

            void Collect(ConcurrentDictionary<(string MethodName, string ParameterName, string HelpLink), DocEnum> enums, bool isMethod)
            {
                foreach (var item in enums)
                {
                    if (!uniqueEnums.TryGetValue(item.Value, out List<(string MethodName, string ParameterName, string HelpLink, bool IsMethod)>? list))
                    {
                        uniqueEnums.Add(item.Value, list = new());
                    }

                    list.Add((item.Key.MethodName, item.Key.ParameterName, item.Key.HelpLink, isMethod));

                    foreach (KeyValuePair<string, (ulong? Value, string? Doc)> enumValue in item.Value.Members)
                    {
                        if (enumValue.Value.Doc is object)
                        {
                            if (!constantsDocs.TryGetValue(enumValue.Key, out List<(string MethodName, string HelpLink, string Doc)>? values))
                            {
                                constantsDocs.Add(enumValue.Key, values = new());
                            }

                            values.Add((item.Key.MethodName, item.Key.HelpLink, enumValue.Value.Doc));
                        }
                    }
                }
            }

            Collect(parameterEnums, isMethod: true);
            Collect(fieldEnums, isMethod: false);

            foreach (var item in constantsDocs)
            {
                string doc = item.Value[0].Doc;

                // If the documentation varies across methods, just link to each document.
                bool differenceDetected = false;
                for (int i = 1; i < item.Value.Count; i++)
                {
                    if (item.Value[i].Doc != doc)
                    {
                        differenceDetected = true;
                        break;
                    }
                }

                var docNode = new YamlMappingNode();
                if (differenceDetected)
                {
                    doc = "Documentation varies per use. Refer to each: " + string.Join(", ", item.Value.Select(v => @$"<see href=""{v.HelpLink}"">{v.MethodOrStructName}</see>")) + ".";
                }
                else
                {
                    // Just point to any arbitrary method that documents it.
                    docNode.Add("HelpLink", item.Value[0].HelpLink);
                }

                docNode.Add("Description", doc);

                if (!IsYamlProblematicKey(item.Key))
                {
                    results.TryAdd(new YamlScalarNode(item.Key), docNode);
                }
            }

            if (this.EmitEnums)
            {
                string enumDirectory = Path.GetDirectoryName(this.outputPath) ?? throw new InvalidOperationException("Unable to determine where to write enums.");
                Directory.CreateDirectory(enumDirectory);
                using var enumsJsonStream = File.OpenWrite(Path.Combine(enumDirectory, "enums.json"));
                using var writer = new Utf8JsonWriter(enumsJsonStream, new JsonWriterOptions { Indented = true });
                writer.WriteStartArray();

                foreach (KeyValuePair<DocEnum, List<(string MethodName, string ParameterName, string HelpLink, bool IsMethod)>> item in uniqueEnums)
                {
                    writer.WriteStartObject();

                    if (item.Key.GetRecommendedName(item.Value) is string enumName)
                    {
                        writer.WriteString("name", enumName);
                    }

                    writer.WriteBoolean("flags", item.Key.IsFlags);

                    writer.WritePropertyName("members");
                    writer.WriteStartArray();
                    foreach (var member in item.Key.Members)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", member.Key);
                        if (member.Value.Value is ulong value)
                        {
                            writer.WriteString("value", value.ToString(CultureInfo.InvariantCulture));
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();

                    writer.WritePropertyName("uses");
                    writer.WriteStartArray();
                    foreach (var uses in item.Value)
                    {
                        writer.WriteStartObject();

                        int periodIndex = uses.MethodName.IndexOf('.', StringComparison.Ordinal);
                        string? iface = periodIndex >= 0 ? uses.MethodName.Substring(0, periodIndex) : null;
                        string name = periodIndex >= 0 ? uses.MethodName.Substring(periodIndex + 1) : uses.MethodName;

                        if (iface is string)
                        {
                            writer.WriteString("interface", iface);
                        }

                        writer.WriteString(uses.IsMethod ? "method" : "struct", name);
                        writer.WriteString(uses.IsMethod ? "parameter" : "field", uses.ParameterName);

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            return constantsDocs.Count;
        }

        private void Worker(CancellationToken cancellationToken)
        {
            Console.WriteLine("Enumerating documents to be parsed...");
            string[] paths = Directory.GetFiles(this.contentBasePath, "??-*-*.md", SearchOption.AllDirectories)
                ////.Where(p => p.Contains(@"DNS_RECORDA", StringComparison.OrdinalIgnoreCase)).ToArray()
                ;

            Console.WriteLine("Parsing documents...");
            var timer = Stopwatch.StartNew();
            var parsedNodes = from path in paths.AsParallel()
                              let result = this.ParseDocFile(path)
                              where result is not null
                              select (Path: path, result.Value.ApiName, result.Value.YamlNode, result.Value.EnumsByParameter, result.Value.EnumsByField);
            var results = new ConcurrentDictionary<YamlNode, YamlNode>();
            var parameterEnums = new ConcurrentDictionary<(string MethodName, string ParameterName, string HelpLink), DocEnum>();
            var fieldEnums = new ConcurrentDictionary<(string StructName, string FieldName, string HelpLink), DocEnum>();
            if (Debugger.IsAttached)
            {
                parsedNodes = parsedNodes.WithDegreeOfParallelism(1); // improve debuggability
            }

            parsedNodes
                .WithCancellation<(string Path, string ApiName, YamlNode YamlNode, IReadOnlyDictionary<string, DocEnum> EnumsByParameter, IReadOnlyDictionary<string, DocEnum> EnumsByField)>(cancellationToken)
                .ForAll(result =>
                {
                    results.TryAdd(new YamlScalarNode(result.ApiName), result.YamlNode);
                    foreach (var e in result.EnumsByParameter)
                    {
                        string helpLink = ((YamlScalarNode)result.YamlNode["HelpLink"]).Value!;
                        parameterEnums.TryAdd((result.ApiName, e.Key, helpLink), e.Value);
                    }

                    foreach (var e in result.EnumsByField)
                    {
                        string helpLink = ((YamlScalarNode)result.YamlNode["HelpLink"]).Value!;
                        fieldEnums.TryAdd((result.ApiName, e.Key, helpLink), e.Value);
                    }
                });
            if (paths.Length == 0)
            {
                Console.Error.WriteLine("No documents found to parse.");
            }
            else
            {
                Console.WriteLine("Parsed {2} documents in {0} ({1} per document)", timer.Elapsed, timer.Elapsed / paths.Length, paths.Length);
                Console.WriteLine($"Found {parameterEnums.Count + fieldEnums.Count} enums.");
            }

            Console.WriteLine("Analyzing and naming enums and collecting docs on their members...");
            int constantsCount = this.AnalyzeEnums(results, parameterEnums, fieldEnums);
            Console.WriteLine($"Found docs for {constantsCount} constants.");

            Console.WriteLine("Writing results to \"{0}\"", this.outputPath);
            var yamlDocument = new YamlDocument(new YamlMappingNode(results));
            var yamlStream = new YamlStream(yamlDocument);
            Directory.CreateDirectory(Path.GetDirectoryName(this.outputPath)!);
            using var yamlWriter = File.CreateText(this.outputPath);
            yamlWriter.WriteLine($"# This file was generated by the {Assembly.GetExecutingAssembly().GetName().Name} tool in this repo.");
            yamlStream.Save(yamlWriter);
        }

        private (string ApiName, YamlNode YamlNode, IReadOnlyDictionary<string, DocEnum> EnumsByParameter, IReadOnlyDictionary<string, DocEnum> EnumsByField)? ParseDocFile(string filePath)
        {
            try
            {
                var enumsByParameter = new Dictionary<string, DocEnum>();
                var enumsByField = new Dictionary<string, DocEnum>();
                var yaml = new YamlStream();
                using StreamReader mdFileReader = File.OpenText(filePath);
                using var markdownToYamlReader = new YamlSectionReader(mdFileReader);
                var yamlBuilder = new StringBuilder();
                string? line;
                while ((line = markdownToYamlReader.ReadLine()) is object)
                {
                    yamlBuilder.AppendLine(line);
                }

                try
                {
                    yaml.Load(new StringReader(yamlBuilder.ToString()));
                }
                catch (YamlDotNet.Core.YamlException ex)
                {
                    Debug.WriteLine("YAML parsing error in \"{0}\": {1}", filePath, ex.Message);
                    return null;
                }

                YamlSequenceNode methodNames = (YamlSequenceNode)yaml.Documents[0].RootNode["api_name"];
                bool TryGetProperName(string searchFor, char? suffix, [NotNullWhen(true)] out string? match)
                {
                    if (suffix.HasValue)
                    {
                        if (searchFor.EndsWith(suffix.Value))
                        {
                            searchFor = searchFor.Substring(0, searchFor.Length - 1);
                        }
                        else
                        {
                            match = null;
                            return false;
                        }
                    }

                    match = methodNames.Children.Cast<YamlScalarNode>().FirstOrDefault(c => string.Equals(c.Value?.Replace('.', '-'), searchFor, StringComparison.OrdinalIgnoreCase))?.Value;

                    if (suffix.HasValue && match is object)
                    {
                        match += char.ToUpper(suffix.Value, CultureInfo.InvariantCulture);
                    }

                    return match is object;
                }

                string presumedMethodName = FileNamePattern.Match(Path.GetFileNameWithoutExtension(filePath)).Groups[1].Value;

                // Some structures have filenames that include the W or A suffix when the content doesn't. So try some fuzzy matching.
                if (!TryGetProperName(presumedMethodName, null, out string? properName) &&
                    !TryGetProperName(presumedMethodName, 'a', out properName) &&
                    !TryGetProperName(presumedMethodName, 'w', out properName))
                {
                    Debug.WriteLine("WARNING: Could not find proper API name in: {0}", filePath);
                    return null;
                }

                var methodNode = new YamlMappingNode();
                Uri helpLink = new Uri("https://docs.microsoft.com/windows/win32/api/" + filePath.Substring(this.contentBasePath.Length, filePath.Length - 3 - this.contentBasePath.Length).Replace('\\', '/'));
                methodNode.Add("HelpLink", helpLink.AbsoluteUri);

                var description = ((YamlMappingNode)yaml.Documents[0].RootNode).Children.FirstOrDefault(n => n.Key is YamlScalarNode { Value: "description" }).Value as YamlScalarNode;
                if (description is object)
                {
                    methodNode.Add("Description", description);
                }

                // Search for parameter/field docs
                var parametersMap = new YamlMappingNode();
                var fieldsMap = new YamlMappingNode();
                YamlScalarNode? remarksNode = null;
                StringBuilder docBuilder = new StringBuilder();
                line = mdFileReader.ReadLine();

                static string FixupLine(string line)
                {
                    line = line.Replace("href=\"/", "href=\"https://docs.microsoft.com/");
                    line = InlineCodeTag.Replace(line, match => $"<c>{match.Groups[1].Value}</c>");
                    return line;
                }

                void ParseTextSection(out YamlScalarNode node)
                {
                    while ((line = mdFileReader.ReadLine()) is object)
                    {
                        if (line.StartsWith('#'))
                        {
                            break;
                        }

                        line = FixupLine(line);
                        docBuilder.AppendLine(line);
                    }

                    node = new YamlScalarNode(docBuilder.ToString());

                    docBuilder.Clear();
                }

                IReadOnlyDictionary<string, (ulong? Value, string? Doc)> ParseEnumTable()
                {
                    var enums = new Dictionary<string, (ulong? Value, string? Doc)>();
                    int state = 0;
                    const int StateReadingHeader = 0;
                    const int StateReadingName = 1;
                    const int StateLookingForDetail = 2;
                    const int StateReadingDocColumn = 3;
                    string? enumName = null;
                    ulong? enumValue = null;
                    var docsBuilder = new StringBuilder();
                    while ((line = mdFileReader.ReadLine()) is object)
                    {
                        if (line == "</table>")
                        {
                            break;
                        }

                        switch (state)
                        {
                            case StateReadingHeader:
                                // Reading TR header
                                if (line == "</tr>")
                                {
                                    state = StateReadingName;
                                }

                                break;

                            case StateReadingName:
                                // Reading an enum row's name column.
                                Match m = EnumNameCell.Match(line);
                                if (m.Success)
                                {
                                    enumName = m.Groups[1].Value;
                                    if (enumName == "0")
                                    {
                                        enumName = "None";
                                        enumValue = 0;
                                    }

                                    state = StateLookingForDetail;
                                }

                                break;

                            case StateLookingForDetail:
                                // Looking for an enum row's doc column.
                                m = EnumOrdinalValue.Match(line);
                                if (m.Success)
                                {
                                    string value = m.Groups[1].Value;
                                    bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                                    if (hex)
                                    {
                                        value = value.Substring(2);
                                    }

                                    enumValue = ulong.Parse(value, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
                                }
                                else if (line.StartsWith("<td", StringComparison.OrdinalIgnoreCase))
                                {
                                    state = StateReadingDocColumn;
                                }
                                else if (line.Contains("</tr>", StringComparison.OrdinalIgnoreCase))
                                {
                                    // The row ended before we found the doc column.
                                    state = StateReadingName;
                                    enums.Add(enumName!, (enumValue, null));
                                    enumName = null;
                                    enumValue = null;
                                }

                                break;

                            case StateReadingDocColumn:
                                // Reading the enum row's doc column.
                                if (line.StartsWith("</td>", StringComparison.OrdinalIgnoreCase))
                                {
                                    state = StateReadingName;

                                    // Some docs are invalid in documenting the same enum multiple times.
                                    if (!enums.ContainsKey(enumName!))
                                    {
                                        enums.Add(enumName!, (enumValue, docsBuilder.ToString().Trim()));
                                    }

                                    enumName = null;
                                    enumValue = null;
                                    docsBuilder.Clear();
                                    break;
                                }

                                docsBuilder.AppendLine(FixupLine(line));
                                break;
                        }
                    }

                    return enums;
                }

                void ParseSection(Match match, YamlMappingNode receivingMap, bool lookForParameterEnums = false, bool lookForFieldEnums = false)
                {
                    string sectionName = match.Groups[1].Value;
                    bool foundEnum = false;
                    bool foundEnumIsFlags = false;
                    while ((line = mdFileReader.ReadLine()) is object)
                    {
                        if (line.StartsWith('#'))
                        {
                            break;
                        }

                        if (lookForParameterEnums || lookForFieldEnums)
                        {
                            if (foundEnum)
                            {
                                if (line == "<table>")
                                {
                                    IReadOnlyDictionary<string, (ulong? Value, string? Doc)> enumNamesAndDocs = ParseEnumTable();
                                    if (enumNamesAndDocs.Count > 0)
                                    {
                                        var enums = lookForParameterEnums ? enumsByParameter : enumsByField;
                                        if (!enums.ContainsKey(sectionName))
                                        {
                                            enums.Add(sectionName, new DocEnum(foundEnumIsFlags, enumNamesAndDocs));
                                        }
                                    }

                                    lookForParameterEnums = false;
                                    lookForFieldEnums = false;
                                }
                            }
                            else
                            {
                                foundEnum = line.Contains("of the following values", StringComparison.OrdinalIgnoreCase);
                                foundEnumIsFlags = line.Contains("combination of", StringComparison.OrdinalIgnoreCase)
                                    || line.Contains("zero or more of", StringComparison.OrdinalIgnoreCase)
                                    || line.Contains("one or both of", StringComparison.OrdinalIgnoreCase)
                                    || line.Contains("one or more of", StringComparison.OrdinalIgnoreCase);
                            }
                        }

                        if (!foundEnum)
                        {
                            line = FixupLine(line);
                            docBuilder.AppendLine(line);
                        }
                    }

                    try
                    {
                        if (!IsYamlProblematicKey(sectionName))
                        {
                            receivingMap.Add(sectionName, docBuilder.ToString().Trim());
                        }
                    }
                    catch (ArgumentException)
                    {
                    }

                    docBuilder.Clear();
                }

                while (line is object)
                {
                    if (ParameterHeaderPattern.Match(line) is Match { Success: true } parameterMatch)
                    {
                        ParseSection(parameterMatch, parametersMap, lookForParameterEnums: true);
                    }
                    else if (FieldHeaderPattern.Match(line) is Match { Success: true } fieldMatch)
                    {
                        ParseSection(fieldMatch, fieldsMap, lookForFieldEnums: true);
                    }
                    else if (RemarksHeaderPattern.Match(line) is Match { Success: true } remarksMatch)
                    {
                        ParseTextSection(out remarksNode);
                    }
                    else
                    {
                        if (line is object && ReturnHeaderPattern.IsMatch(line))
                        {
                            break;
                        }

                        line = mdFileReader.ReadLine();
                    }
                }

                if (parametersMap.Any())
                {
                    methodNode.Add("Parameters", parametersMap);
                }

                if (fieldsMap.Any())
                {
                    methodNode.Add("Fields", fieldsMap);
                }

                if (remarksNode is object)
                {
                    methodNode.Add("Remarks", remarksNode);
                }

                // Search for return value documentation
                while (line is object)
                {
                    Match m = ReturnHeaderPattern.Match(line);
                    if (m.Success)
                    {
                        while ((line = mdFileReader.ReadLine()) is object)
                        {
                            if (line.StartsWith('#'))
                            {
                                break;
                            }

                            docBuilder.AppendLine(line);
                        }

                        methodNode.Add("ReturnValue", docBuilder.ToString().Trim());
                        docBuilder.Clear();
                        break;
                    }
                    else
                    {
                        line = mdFileReader.ReadLine();
                    }
                }

                return (properName, methodNode, enumsByParameter, enumsByField);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed parsing \"{filePath}\".", ex);
            }
        }

        private class YamlSectionReader : TextReader
        {
            private readonly StreamReader fileReader;
            private bool firstLineRead;
            private bool lastLineRead;

            internal YamlSectionReader(StreamReader fileReader)
            {
                this.fileReader = fileReader;
            }

            public override string? ReadLine()
            {
                if (this.lastLineRead)
                {
                    return null;
                }

                if (!this.firstLineRead)
                {
                    Expect("---", this.fileReader.ReadLine());
                    this.firstLineRead = true;
                }

                string? line = this.fileReader.ReadLine();
                if (line == "---")
                {
                    this.lastLineRead = true;
                    return null;
                }

                return line;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.fileReader.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
