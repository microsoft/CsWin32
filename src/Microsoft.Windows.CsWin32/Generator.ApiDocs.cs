// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    internal Docs? ApiDocs { get; }

    private T AddApiDocumentation<T>(string api, T memberDeclaration)
        where T : MemberDeclarationSyntax
    {
        if (this.ApiDocs is object && this.ApiDocs.TryGetApiDocs(api, out ApiDetails? docs))
        {
            var docCommentsBuilder = new StringBuilder();
            if (docs.Description is object)
            {
                docCommentsBuilder.Append($@"/// <summary>");
                EmitDoc(docs.Description, docCommentsBuilder, docs, string.Empty);
                docCommentsBuilder.AppendLine("</summary>");
            }

            if (docs.Parameters is object)
            {
                if (memberDeclaration is BaseMethodDeclarationSyntax methodDecl)
                {
                    foreach (KeyValuePair<string, string> entry in docs.Parameters)
                    {
                        if (!methodDecl.ParameterList.Parameters.Any(p => string.Equals(p.Identifier.ValueText, entry.Key, StringComparison.Ordinal)))
                        {
                            // Skip documentation for parameters that do not actually exist on the method.
                            continue;
                        }

                        docCommentsBuilder.Append($@"/// <param name=""{entry.Key}"">");
                        EmitDoc(entry.Value, docCommentsBuilder, docs, "parameters");
                        docCommentsBuilder.AppendLine("</param>");
                    }
                }
            }

            if (docs.Fields is object)
            {
                var fieldsDocBuilder = new StringBuilder();
                switch (memberDeclaration)
                {
                    case StructDeclarationSyntax structDeclaration:
                        memberDeclaration = memberDeclaration.ReplaceNodes(
                            structDeclaration.Members.OfType<FieldDeclarationSyntax>(),
                            (_, field) =>
                            {
                                VariableDeclaratorSyntax? variable = field.Declaration.Variables.Single();
                                if (docs.Fields.TryGetValue(variable.Identifier.ValueText, out string? fieldDoc))
                                {
                                    fieldsDocBuilder.Append("/// <summary>");
                                    EmitDoc(fieldDoc, fieldsDocBuilder, docs, "members");
                                    fieldsDocBuilder.AppendLine("</summary>");
                                    if (field.Declaration.Type.HasAnnotations(OriginalDelegateAnnotation))
                                    {
                                        fieldsDocBuilder.AppendLine(@$"/// <remarks>See the <see cref=""{field.Declaration.Type.GetAnnotations(OriginalDelegateAnnotation).Single().Data}"" /> delegate for more about this function.</remarks>");
                                    }

                                    field = field.WithLeadingTrivia(ParseLeadingTrivia(fieldsDocBuilder.ToString().Replace("\r\n", "\n")));
                                    fieldsDocBuilder.Clear();
                                }

                                return field;
                            });
                        break;
                    case EnumDeclarationSyntax enumDeclaration:
                        memberDeclaration = memberDeclaration.ReplaceNodes(
                            enumDeclaration.Members,
                            (_, field) =>
                            {
                                if (docs.Fields.TryGetValue(field.Identifier.ValueText, out string? fieldDoc))
                                {
                                    fieldsDocBuilder.Append($@"/// <summary>");
                                    EmitDoc(fieldDoc, fieldsDocBuilder, docs, "members");
                                    fieldsDocBuilder.AppendLine("</summary>");
                                    field = field.WithLeadingTrivia(ParseLeadingTrivia(fieldsDocBuilder.ToString().Replace("\r\n", "\n")));
                                    fieldsDocBuilder.Clear();
                                }

                                return field;
                            });
                        break;
                }
            }

            if (docs.ReturnValue is object)
            {
                docCommentsBuilder.Append("/// <returns>");
                EmitDoc(docs.ReturnValue, docCommentsBuilder, docs: null, string.Empty);
                docCommentsBuilder.AppendLine("</returns>");
            }

            if (docs.Remarks is object || docs.HelpLink is object)
            {
                docCommentsBuilder.Append($"/// <remarks>");
                if (docs.Remarks is object)
                {
                    EmitDoc(docs.Remarks, docCommentsBuilder, docs, string.Empty);
                }
                else if (docs.HelpLink is object)
                {
                    docCommentsBuilder.AppendLine();
                    docCommentsBuilder.AppendLine($@"/// <para><see href=""{docs.HelpLink}"">Learn more about this API from learn.microsoft.com</see>.</para>");
                    docCommentsBuilder.Append("/// ");
                }

                docCommentsBuilder.AppendLine($"</remarks>");
            }

            memberDeclaration = memberDeclaration.WithLeadingTrivia(
                ParseLeadingTrivia(docCommentsBuilder.ToString().Replace("\r\n", "\n")));
        }

        return memberDeclaration;

        static void EmitLine(StringBuilder stringBuilder, string yamlDocSrc)
        {
            stringBuilder.Append(yamlDocSrc.Trim());
        }

        static void EmitDoc(string yamlDocSrc, StringBuilder docCommentsBuilder, ApiDetails? docs, string docsAnchor)
        {
            if (yamlDocSrc.Contains('\n'))
            {
                docCommentsBuilder.AppendLine();
                var docReader = new StringReader(yamlDocSrc);
                string? paramDocLine;

                bool inParagraph = false;
                bool inComment = false;
                int blankLineCounter = 0;
                while ((paramDocLine = docReader.ReadLine()) is object)
                {
                    if (string.IsNullOrWhiteSpace(paramDocLine))
                    {
                        if (++blankLineCounter >= 2 && inParagraph)
                        {
                            docCommentsBuilder.AppendLine("</para>");
                            inParagraph = false;
                            inComment = false;
                        }

                        continue;
                    }
                    else if (blankLineCounter > 0)
                    {
                        blankLineCounter = 0;
                    }
                    else if (docCommentsBuilder.Length > 0 && docCommentsBuilder[docCommentsBuilder.Length - 1] != '\n')
                    {
                        docCommentsBuilder.Append(' ');
                    }

                    if (inParagraph)
                    {
                        if (docCommentsBuilder.Length > 0 && docCommentsBuilder[docCommentsBuilder.Length - 1] is not (' ' or '\n'))
                        {
                            docCommentsBuilder.Append(' ');
                        }
                    }
                    else
                    {
                        docCommentsBuilder.Append("/// <para>");
                        inParagraph = true;
                        inComment = true;
                    }

                    if (!inComment)
                    {
                        docCommentsBuilder.Append("/// ");
                    }

                    if (paramDocLine.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("```", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        paramDocLine.IndexOf("<<", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // We don't try to format tables, so truncate at this point.
                        if (inParagraph)
                        {
                            docCommentsBuilder.AppendLine("</para>");
                            inParagraph = false;
                            inComment = false;
                        }

                        docCommentsBuilder.AppendLine($@"/// <para>This doc was truncated.</para>");

                        break; // is this the right way?
                    }

                    EmitLine(docCommentsBuilder, paramDocLine);
                }

                if (inParagraph)
                {
                    if (!inComment)
                    {
                        docCommentsBuilder.Append("/// ");
                    }

                    docCommentsBuilder.AppendLine("</para>");
                    inParagraph = false;
                    inComment = false;
                }

                if (docs is object)
                {
                    docCommentsBuilder.AppendLine($@"/// <para><see href=""{docs.HelpLink}#{docsAnchor}"">Read more on learn.microsoft.com</see>.</para>");
                }

                docCommentsBuilder.Append("/// ");
            }
            else
            {
                EmitLine(docCommentsBuilder, yamlDocSrc);
            }
        }
    }
}
