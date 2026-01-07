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

            // Ensure a new line before writing a comment if needed
            if ((output.Length > 0) && !output.IsNewLine())
            {
                output.Append(Environment.NewLine);
            }

            if (content.Contains("<") && content.Contains(">"))
            {
                // =========================================================
                // Extension behavior:
                // When TreatCommentWithTagAsMultiline is enabled,
                // comments that contain XAML tags are formatted as
                // multi-line block comments.
                //
                // Existing behavior MUST remain unchanged otherwise.
                // =========================================================
                bool isMultilineCommentWithTag =
                    this.options.TreatCommentWithTagAsMultiline
                    && !string.IsNullOrEmpty(content)
                    && content.StartsWith("<", StringComparison.Ordinal)
                    && (content.IndexOf('\n') >= 0 || content.IndexOf('\r') >= 0);

                if (isMultilineCommentWithTag)
                {
                    output.Append(currentIndentString);
                    output.Append("<!--");
                    output.Append(Environment.NewLine);

                    // One indentation level according to current settings
                    var oneIndent = this.indentService.GetIndentString(1);

                    // Normalize comment content lines:
                    //  - trim trailing whitespace
                    //  - remove empty lines
                    var lines = content
                        .GetLines()
                        .Select(l => l.TrimEnd())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToArray();

                    // State used to keep "hanging aligned attributes"
                    // for an open start tag (e.g. <Image ...>)
                    string pendingAlignPrefix = string.Empty;
                    bool pendingOpen = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var trimmedStart = lines[i].TrimStart();
                        bool isTagLine = trimmedStart.StartsWith("<", StringComparison.Ordinal);

                        // -----------------------------------------------------
                        // 1) While a start tag is still open, attribute lines
                        //    must be aligned using hanging indent.
                        // -----------------------------------------------------
                        if (pendingOpen && !isTagLine && trimmedStart.IndexOf('=') >= 0)
                        {
                            bool closedHere;
                            var attrs = ExtractAttributes(trimmedStart, out closedHere);

                            // Fallback to default behavior if parsing failed
                            if (attrs.Length == 0)
                            {
                                output.Append(currentIndentString);
                                output.Append(oneIndent);
                                output.Append(trimmedStart);
                                output.Append(Environment.NewLine);

                                if (EndsWithTagClose(trimmedStart))
                                    pendingOpen = false;

                                continue;
                            }

                            foreach (var attr in attrs)
                            {
                                output.Append(currentIndentString);
                                output.Append(pendingAlignPrefix);
                                output.Append(attr);
                                output.Append(Environment.NewLine);
                            }

                            if (closedHere)
                                pendingOpen = false;

                            continue;
                        }

                        // -----------------------------------------------------
                        // 2) Detect a start-tag header line such as "<Image"
                        //    followed by attributes on the next line.
                        //    Reconstruct it into aligned multi-line form.
                        // -----------------------------------------------------
                        if (isTagLine
                            && trimmedStart.IndexOf('>') < 0
                            && trimmedStart.IndexOf('=') < 0
                            && i + 1 < lines.Length)
                        {
                            var next = lines[i + 1].TrimStart();
                            if (!next.StartsWith("<", StringComparison.Ordinal) && next.IndexOf('=') >= 0)
                            {
                                string tagName = TryGetTagNameFromStartTagLine(trimmedStart);
                                if (!string.IsNullOrEmpty(tagName))
                                {
                                    bool closedHere;
                                    var attrs = ExtractAttributes(next, out closedHere);
                                    if (attrs.Length > 0)
                                    {
                                        string alignPrefix =
                                            new string(' ', ("<" + tagName + " ").Length);

                                        // First line: <Tag attr1
                                        output.Append(currentIndentString);
                                        output.Append("<");
                                        output.Append(tagName);
                                        output.Append(" ");
                                        output.Append(attrs[0]);
                                        output.Append(Environment.NewLine);

                                        // Remaining attributes, aligned
                                        for (int ai = 1; ai < attrs.Length; ai++)
                                        {
                                            output.Append(currentIndentString);
                                            output.Append(alignPrefix);
                                            output.Append(attrs[ai]);
                                            output.Append(Environment.NewLine);
                                        }

                                        i++; // consume attribute line

                                        pendingOpen = !closedHere;
                                        pendingAlignPrefix = alignPrefix;
                                        continue;
                                    }
                                }
                            }
                        }

                        // -----------------------------------------------------
                        // 3) Start tag with attributes but not yet closed
                        //    e.g. "<Image Width="20""
                        // -----------------------------------------------------
                        if (isTagLine
                            && trimmedStart.IndexOf('=') >= 0
                            && trimmedStart.IndexOf('>') < 0)
                        {
                            string tagName = TryGetTagNameFromStartTagLine(trimmedStart);
                            if (!string.IsNullOrEmpty(tagName))
                            {
                                pendingOpen = true;
                                pendingAlignPrefix =
                                    new string(' ', ("<" + tagName + " ").Length);
                            }

                            output.Append(currentIndentString);
                            output.Append(trimmedStart);
                            output.Append(Environment.NewLine);
                            continue;
                        }

                        // -----------------------------------------------------
                        // 4) Single-line closed start tag
                        //    e.g. "<Image Width="20" Height="20" />"
                        // -----------------------------------------------------
                        if (isTagLine && TryFormatStartTagAlignedAttributes(trimmedStart, out var formatted))
                        {
                            foreach (var line in formatted)
                            {
                                output.Append(currentIndentString);
                                output.Append(line);
                                output.Append(Environment.NewLine);
                            }
                            continue;
                        }

                        // Reset alignment state if another tag starts
                        if (pendingOpen && isTagLine)
                        {
                            pendingOpen = false;
                            pendingAlignPrefix = string.Empty;
                        }

                        // -----------------------------------------------------
                        // 5) Default behavior:
                        //    text lines are indented one level deeper
                        // -----------------------------------------------------
                        output.Append(currentIndentString);
                        if (!isTagLine)
                            output.Append(oneIndent);

                        output.Append(trimmedStart);
                        output.Append(Environment.NewLine);
                    }

                    // Close comment block
                    output.Append(currentIndentString);
                    output.Append("-->");
                    return;
                }

                // =========================================================
                // Single-line tag comment:
                //   <!--<Setter ... />-->
                //   -> expanded to a multi-line block when option is enabled
                // =========================================================
                bool isSingleLineCommentWithTag =
                    this.options.TreatCommentWithTagAsMultiline
                    && !string.IsNullOrEmpty(content)
                    && content.StartsWith("<", StringComparison.Ordinal)
                    && (content.IndexOf('\n') < 0 && content.IndexOf('\r') < 0);

                if (isSingleLineCommentWithTag)
                {
                    output.Append(currentIndentString);
                    output.Append("<!--");
                    output.Append(Environment.NewLine);

                    output.Append(currentIndentString);
                    output.Append(content.Trim());
                    output.Append(Environment.NewLine);

                    output.Append(currentIndentString);
                    output.Append("-->");
                    return;
                }

                // =========================================================
                // Original behavior (unchanged)
                // =========================================================
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
                FixIndentBeforeCommentClose(output, this.options, currentIndentString);
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

        // ---------------------------------------------------------------------
        // Fix indentation when "-->" is preceded only by tab characters.
        // This preserves existing behavior while preventing malformed output.
        // ---------------------------------------------------------------------
        private static void FixIndentBeforeCommentClose(
            StringBuilder output,
            IStylerOptions options,
            string currentIndentString)
        {
            if (!options.TreatCommentWithTagAsMultiline)
                return;

            int closePos = output.Length - 3;
            if (closePos < 0) return;

            int nl = LastIndexOf(output, '\n', closePos);
            if (nl < 0) return;

            int segStart = nl + 1;
            int segEnd = closePos;

            if (segEnd <= segStart) return;

            bool hasTab = false;
            for (int i = segStart; i < segEnd; i++)
            {
                if (output[i] == '\t')
                    hasTab = true;
                else
                    return;
            }

            if (!hasTab) return;

            output.Remove(segStart, segEnd - segStart);
            output.Insert(segStart, currentIndentString);
        }

        private static int LastIndexOf(StringBuilder sb, char ch, int startIndex)
        {
            for (int i = startIndex; i >= 0; i--)
            {
                if (sb[i] == ch) return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static bool EndsWithTagClose(string s)
        {
            var t = s.TrimEnd();
            return t.EndsWith("/>", StringComparison.Ordinal)
                || t.EndsWith(">", StringComparison.Ordinal);
        }

        // Extract tag name from a start-tag line.
        // Returns empty string if not applicable (C# 7.3 compatible).
        private static string TryGetTagNameFromStartTagLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != '<') return string.Empty;

            i++;
            if (i < s.Length && (s[i] == '/' || s[i] == '?' || s[i] == '!'))
                return string.Empty;

            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int start = i;

            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '>' || c == '/')
                    break;
                i++;
            }

            return (i > start) ? s.Substring(start, i - start) : string.Empty;
        }

        // Split attributes, respecting quoted values.
        private static string[] ExtractAttributes(string line, out bool closedHere)
        {
            closedHere = false;
            if (string.IsNullOrEmpty(line)) return new string[0];

            string trimmed = line.Trim();
            string closing = string.Empty;

            if (trimmed.EndsWith("/>", StringComparison.Ordinal))
            {
                closing = "/>";
                closedHere = true;
                trimmed = trimmed.Substring(0, trimmed.Length - 2).TrimEnd();
            }
            else if (trimmed.EndsWith(">", StringComparison.Ordinal))
            {
                closing = ">";
                closedHere = true;
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            }

            var tokens = SplitBySpacesRespectingQuotes(trimmed);
            var attrs = tokens.Where(a => a.IndexOf('=') >= 0).ToList();

            if (attrs.Count == 0) return new string[0];

            if (!string.IsNullOrEmpty(closing))
            {
                attrs[attrs.Count - 1] += " " + closing;
            }

            return attrs.ToArray();
        }

        // Format a closed single-line start tag into aligned multi-line form.
        private static bool TryFormatStartTagAlignedAttributes(
            string trimmedStart,
            out string[] lines)
        {
            lines = new string[0];
            if (trimmedStart.IndexOf('=') < 0) return false;

            bool closedHere;
            var attrs = ExtractAttributes(trimmedStart, out closedHere);
            if (!closedHere || attrs.Length < 2) return false;

            string tagName = TryGetTagNameFromStartTagLine(trimmedStart);
            if (string.IsNullOrEmpty(tagName)) return false;

            string alignPrefix = new string(' ', ("<" + tagName + " ").Length);

            var result = new string[attrs.Length];
            result[0] = "<" + tagName + " " + attrs[0];
            for (int i = 1; i < attrs.Length; i++)
            {
                result[i] = alignPrefix + attrs[i];
            }

            lines = result;
            return true;
        }

        // Split a string by spaces while preserving quoted segments.
        // NOTE: Only one implementation must exist in this class.
        private static System.Collections.Generic.List<string>
            SplitBySpacesRespectingQuotes(string s)
        {
            var list = new System.Collections.Generic.List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                list.Add(sb.ToString());

            return list;
        }
    }
}
