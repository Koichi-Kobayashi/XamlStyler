// (c) Xavalon. All rights reserved.

using System;
using System.Linq;
using System.Text;
using System.Xml;
using Xavalon.XamlStyler.Extensions;
using Xavalon.XamlStyler.Options;
using Xavalon.XamlStyler.Parser;
using Xavalon.XamlStyler.Services;

namespace Xavalon.XamlStyler.DocumentProcessors
{
    internal class CommentDocumentProcessor : IDocumentProcessor
    {
        private readonly IStylerOptions options;
        private readonly IndentService indentService;

        public CommentDocumentProcessor(IStylerOptions options, IndentService indentService)
        {
            this.options = options;
            this.indentService = indentService;
        }

        public void Process(XmlReader xmlReader, StringBuilder output, ElementProcessContext elementProcessContext)
        {
            elementProcessContext.UpdateParentElementProcessStatus(ContentTypes.Mixed);

            string currentIndentString = this.indentService.GetIndentString(xmlReader.Depth);
            string content = xmlReader.Value;

            if ((output.Length > 0) && !output.IsNewLine())
            {
                output.Append(Environment.NewLine);
            }

            // Check if comment starts with '<' and option is enabled
            bool isMultilineCommentWithTag = this.options.TreatCommentWithTagAsMultiline 
                && !string.IsNullOrWhiteSpace(content) 
                && content.TrimStart().StartsWith("<", StringComparison.Ordinal);

            if (isMultilineCommentWithTag)
            {
                // Treat comment with tag as multiline: ensure --> is on a new line
                output.Append(currentIndentString);
                output.Append("<!--");

                if (content.Contains("\n"))
                {
                    // Add newline after <!-- if content starts with '<'
                    output.Append(Environment.NewLine);
                    var contentIndentString = this.indentService.GetIndentString(xmlReader.Depth + 1);
                    var lines = content.GetLines().Select(_ => _.TrimEnd(' ')).Where(_ => !string.IsNullOrWhiteSpace(_)).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        bool isFirstOrLast = (i == 0) || (i == lines.Count - 1);
                        string indent = isFirstOrLast ? currentIndentString : contentIndentString;
                        output.Append(indent).Append(line.TrimStart()).Append(Environment.NewLine);
                    }

                    // Ensure newline before --> for multiline comments with tags
                    output.Append(currentIndentString);
                }
                else
                {
                    // Add newline after <!-- if content starts with '<'
                    output.Append(Environment.NewLine);
                    output.Append(currentIndentString).Append(content.TrimStart());
                    // Ensure newline before --> for single-line comments with tags
                    output.Append(Environment.NewLine).Append(currentIndentString);
                }

                output.Append("-->");
            }
            else if (content.Contains("<") && content.Contains(">"))
            {
                output.Append(currentIndentString);
                output.Append("<!--");

                if (content.Contains("\n"))
                {
                    output.Append(String.Join(Environment.NewLine, content.GetLines().Select(_ => _.TrimEnd(' '))));

                    if (content.TrimEnd(' ').EndsWith("\n", StringComparison.Ordinal))
                    {
                        output.Append(currentIndentString);
                    }
                }
                else
                {
                    output.Append(content);
                }

                output.Append("-->");
            }
            else if (content.Contains("#region") || content.Contains("#endregion"))
            {
                output.Append(currentIndentString).Append("<!--").Append(content.Trim()).Append("-->");
            }
            else if (content.Contains("\n"))
            {
                output.Append(currentIndentString).Append("<!--");

                var contentIndentString = this.indentService.GetIndentString(xmlReader.Depth + 1);
                foreach (var line in content.Trim().GetLines())
                {
                    output.Append(Environment.NewLine).Append(contentIndentString).Append(line.Trim());
                }

                output.Append(Environment.NewLine).Append(currentIndentString).Append("-->");
            }
            else
            {
                output
                    .Append(currentIndentString)
                    .Append("<!--")
                    .Append(' ', this.options.CommentSpaces)
                    .Append(content.Trim())
                    .Append(' ', this.options.CommentSpaces)
                    .Append("-->");
            }
        }
    }
}