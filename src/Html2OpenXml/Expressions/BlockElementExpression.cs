/* Copyright (C) Olivier Nizet https://github.com/onizet/html2openxml - All Rights Reserved
 * 
 * This source is subject to the Microsoft Permissive License.
 * Please see the License.txt file for more information.
 * All other rights reserved.
 * 
 * THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
 * KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
 * PARTICULAR PURPOSE.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AngleSharp.Html.Dom;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HtmlToOpenXml.Expressions;

/// <summary>
/// Process the parsing of block contents (like <c>p</c>, <c>span</c>, <c>heading</c>).
/// A block-level element always starts on a new line, and the browsers automatically add some space (a margin) before and after the element.
/// </summary>
class BlockElementExpression(IHtmlElement node, params OpenXmlLeafElement[]? styleProperty) : PhrasingElementExpression(node)
{
    private readonly OpenXmlLeafElement[]? defaultStyleProperties = styleProperty;
    protected readonly ParagraphProperties paraProperties = new();


    /// <inheritdoc/>
    public override IEnumerable<OpenXmlElement> Interpret (ParsingContext context)
    {
        var elements = base.Interpret(context);

        var bookmarkTarget = node.GetAttribute(InternalNamespaceUri, "bookmark");
        if (bookmarkTarget is not null)
        {
            var bookmarkId = IncrementBookmarkId(context).ToString(CultureInfo.InvariantCulture);
            var p = elements.First();
            // need to be inserted after pPr to avoid schema warning
            p.InsertAfter(new BookmarkStart() { Id = bookmarkId, Name = bookmarkTarget }, p.GetFirstChild<ParagraphProperties>());
            p.AppendChild(new BookmarkEnd() { Id = bookmarkId });
        }

        return elements;
    }

    protected override IEnumerable<OpenXmlElement> Interpret (
        ParsingContext context, IEnumerable<AngleSharp.Dom.INode> childNodes)
    {
        return ComposeChildren(context, childNodes, paraProperties,
            (runs) => {
                if ("always".Equals(styleAttributes!["page-break-before"], StringComparison.OrdinalIgnoreCase))
                {
                    runs.Add(
                        new Run(
                            new Break() { Type = BreakValues.Page })
                    );
                    runs.Add(new Run(
                        new LastRenderedPageBreak())
                    );
                }
            },
            (runs) => {
                if ("always".Equals(styleAttributes!["page-break-after"], StringComparison.OrdinalIgnoreCase))
                {
                    runs.Add(new Run(
                        new Break() { Type = BreakValues.Page }));
                }
            });
    }

    public override void CascadeStyles(OpenXmlElement element)
    {
        base.CascadeStyles(element);
        if (!paraProperties.HasChildren || element is not Paragraph paragraph)
            return;

        paragraph.ParagraphProperties ??= new ParagraphProperties();

        var knownTags = new HashSet<string>();
        foreach (var prop in paragraph.ParagraphProperties)
        {
            if (!knownTags.Contains(prop.LocalName))
                knownTags.Add(prop.LocalName);
        }

        foreach (var prop in paraProperties)
        {
            if (!knownTags.Contains(prop.LocalName))
                paragraph.ParagraphProperties.AddChild(prop.CloneNode(true));
        }
    }

    /// <inheritdoc/>
    protected override void ComposeStyles (ParsingContext context)
    {
        base.ComposeStyles(context);

        if (defaultStyleProperties != null)
        {
            foreach (var prop in defaultStyleProperties)
                paraProperties.AddChild(prop.CloneNode(true));
        }

        if (node.Language != null && node.Language != node.Owner!.Body!.Language)
        {
            var ci = Converter.ToLanguage(node.Language);
            if (ci != null)
            {
                bool rtl = ci.TextInfo.IsRightToLeft;

                var lang = new Languages() { Val = ci.TwoLetterISOLanguageName };
                if (rtl) lang.Bidi = ci.Name;

                paraProperties.ParagraphMarkRunProperties = new ParagraphMarkRunProperties(lang);
                paraProperties.BiDi = new BiDi() { Val = OnOffValue.FromBoolean(rtl) };
            }
        }

        // according to w3c, dir should be used in conjonction with lang. But whatever happens, we'll apply the RTL layout
        var dir = node.GetTextDirection();
        if (dir.HasValue) {
            paraProperties.BiDi = new() {
                Val = OnOffValue.FromBoolean(dir == AngleSharp.Dom.DirectionMode.Rtl)
            };
        }

        var attrValue = styleAttributes!["text-align"];
        JustificationValues? align = Converter.ToParagraphAlign(attrValue);
        if (align.HasValue)
        {
            paraProperties.Justification = new() { Val = align };
        }


        var styleBorder = styleAttributes.GetBorders();
        if (!styleBorder.IsEmpty)
        {
            var borders = new ParagraphBorders {
                LeftBorder = Converter.ToBorder<LeftBorder>(styleBorder.Left),
                RightBorder = Converter.ToBorder<RightBorder>(styleBorder.Right),
                TopBorder = Converter.ToBorder<TopBorder>(styleBorder.Top),
                BottomBorder = Converter.ToBorder<BottomBorder>(styleBorder.Bottom)
            };

            paraProperties.ParagraphBorders = borders;
        }

        foreach (string className in node.ClassList)
        {
            var matchClassName = context.DocumentStyle.GetStyle(className, StyleValues.Paragraph, ignoreCase: true);
            if (matchClassName != null)
            {
                paraProperties.ParagraphStyleId = new ParagraphStyleId() { Val = matchClassName };
                break;
            }
        }

        Margin margin = styleAttributes.GetMargin("margin");
        Indentation? indentation = null;
        if (!margin.IsEmpty)
        {
            if (margin.Top.IsFixed || margin.Bottom.IsFixed)
            {
                var spacing = new SpacingBetweenLines();
                if (margin.Top.IsFixed) spacing.Before = margin.Top.ValueInDxa.ToString(CultureInfo.InvariantCulture);
                if (margin.Bottom.IsFixed) spacing.After = margin.Bottom.ValueInDxa.ToString(CultureInfo.InvariantCulture);
                paraProperties.SpacingBetweenLines = spacing;
            }
            if (margin.Left.IsFixed || margin.Right.IsFixed)
            {
                indentation = new Indentation();
                if (margin.Left.IsFixed) indentation.Left = margin.Left.ValueInDxa.ToString(CultureInfo.InvariantCulture);
                if (margin.Right.IsFixed) indentation.Right = margin.Right.ValueInDxa.ToString(CultureInfo.InvariantCulture);
                paraProperties.Indentation = indentation;
            }
        }

        // implemented by giorand (feature #13787)
        Unit textIndent = styleAttributes.GetUnit("text-indent");
        if (textIndent.IsValid)
        {
            indentation ??= new Indentation();
            indentation.FirstLine = Math.Max(0, textIndent.ValueInDxa).ToString(CultureInfo.InvariantCulture);
            paraProperties.Indentation = indentation;
        }

        // support left and right padding
        var padding = styleAttributes.GetMargin("padding");
        if (!padding.IsEmpty && (padding.Left.IsFixed || padding.Right.IsFixed))
        {
            indentation ??= new Indentation();
            if (padding.Left.Value > 0) indentation.Left = padding.Left.ValueInDxa.ToString(CultureInfo.InvariantCulture);
            if (padding.Right.Value > 0) indentation.Right = padding.Right.ValueInDxa.ToString(CultureInfo.InvariantCulture);

            paraProperties.Indentation = indentation;
        }

        var lineHeight = styleAttributes.GetUnit("line-height");
        if (!lineHeight.IsValid 
            && "normal".Equals(styleAttributes["line-height"], StringComparison.OrdinalIgnoreCase))
        {
            // if `normal` is specified, reset any values
            lineHeight = new Unit(UnitMetric.Unitless, 1);
        }

        if (lineHeight.IsValid)
        {
            if (lineHeight.Type == UnitMetric.Unitless)
            {
                // auto should be considered as 240ths of a line
                // https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.spacingbetweenlines.line?view=openxml-3.0.1
                paraProperties.SpacingBetweenLines = new() {
                    LineRule = LineSpacingRuleValues.Auto,
                    Line = Math.Round(lineHeight.Value * 240).ToString(CultureInfo.InvariantCulture)
                };
            }
            else if (lineHeight.Type == UnitMetric.Percent)
            {
                // percentage depends on the font size which is hard to determine here
                // let's rely this to "auto" behaviour
                paraProperties.SpacingBetweenLines = new() {
                    LineRule = LineSpacingRuleValues.Auto,
                    Line = Math.Round(lineHeight.Value / 100 * 240).ToString(CultureInfo.InvariantCulture)
                };
            }
            else
            {
                // twentieths of a point
                paraProperties.SpacingBetweenLines = new() {
                    LineRule = LineSpacingRuleValues.Exact,
                    Line = Math.Round(lineHeight.ValueInPoint * 20).ToString(CultureInfo.InvariantCulture)
                };
            }
        }
    }

    /// <summary>
    /// Intrepret all the child nodes and combine them.
    /// </summary>
    /// <param name="context">The child parsing context.</param>
    /// <param name="childNodes">The list of child nodes.</param>
    /// <param name="paragraphProperties">The parent paragraph properties to apply.</param>
    /// <param name="preAction">Optionally insert new runs at the beginning of the processing.</param>
    /// <param name="postAction">Optionally insert new runs at the end of the processing.</param>
    internal static IEnumerable<OpenXmlElement> ComposeChildren(ParsingContext context, 
        IEnumerable<AngleSharp.Dom.INode> childNodes,
        ParagraphProperties paragraphProperties,
        Action<IList<OpenXmlElement>>? preAction = null,
        Action<IList<OpenXmlElement>>? postAction = null)
    {
        var runs = new List<OpenXmlElement>();
        var flowElements = new List<OpenXmlElement>();

        preAction?.Invoke(runs);

        OpenXmlElement? previousElement = null;
        foreach (var child in childNodes)
        {
            var expression = CreateFromHtmlNode (child);
            if (expression == null) continue;

            foreach (var element in expression.Interpret(context))
            {
                context.CascadeStyles(element);
                if (element is Run r || element is Hyperlink)
                {
                    runs.Add(element);
                    continue;
                }
                // if 2 tables are consectuives, we insert a paragraph in between
                // or Word will merge the two tables
                else if (element is Table && previousElement is Table)
                {
                    flowElements.Add(new Paragraph());
                }

                if (runs.Count > 0)
                {
                    flowElements.Add(CreateParagraph(context, runs, paragraphProperties));
                    runs.Clear();
                }

                previousElement = element;
                flowElements.Add(element);
            }
        }

        postAction?.Invoke(runs);

        if (runs.Count > 0)
            flowElements.Add(CreateParagraph(context, runs, paragraphProperties));

        return flowElements;
    }

    /// <summary>
    /// Create a new Paragraph and combine all the runs.
    /// </summary>
    private static Paragraph CreateParagraph(ParsingContext context, IList<OpenXmlElement> runs, ParagraphProperties paraProperties)
    {
        Paragraph p = new();
        if (paraProperties.HasChildren)
            p.ParagraphProperties = (ParagraphProperties) paraProperties.CloneNode(true);

        context.CascadeStyles(p);

        p.Append(CombineRuns(runs));
        return p;
    }

    /// <summary>
    /// Resolve the next available <see cref="BookmarkStart.Id"/> (they must be unique).
    /// </summary>
    private static int IncrementBookmarkId(ParsingContext context)
    {
        var bookmarkRef = context.Properties<int?>("bookmarkRef");

        if (!bookmarkRef.HasValue)
        {
            bookmarkRef = 0;
            foreach (var b in context.MainPart.Document.Body!.Descendants<BookmarkStart>())
            {
                // OpenXml SDK expose the ID as a string value but this is really an integer
                if (b.Id != null && int.TryParse(b.Id.Value, out int id) && id > bookmarkRef)
                    bookmarkRef = id;
            }
        }

        bookmarkRef++;
        context.Properties("bookmarkRef", bookmarkRef);
        return bookmarkRef.Value;
    }
}