using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowFloatIA;

// A native, text-only Markdown view: no browser, HTML execution or remote images.
public sealed class MarkdownTextBlock : RichTextBox
{
    private readonly Paragraph _paragraph = new() { Margin = new Thickness(0), LineHeight = 21 };
    public InlineCollection Inlines => _paragraph.Inlines;
    public TextPointer ContentStart => _paragraph.ContentStart;
    public TextPointer ContentEnd => _paragraph.ContentEnd;
    public MarkdownTextBlock()
    {
        IsReadOnly = true; IsDocumentEnabled = true; IsUndoEnabled = false;
        IsReadOnlyCaretVisible = true; IsInactiveSelectionHighlightEnabled = true;
        BorderThickness = new Thickness(0); Padding = new Thickness(0);
        Background = Brushes.Transparent;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = new FlowDocument(_paragraph) { PagePadding = new Thickness(0) };
        SetResourceReference(ForegroundProperty, "TextBrush");
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Copiar seleção", Command = System.Windows.Input.ApplicationCommands.Copy, CommandTarget = this });
        menu.Items.Add(new MenuItem { Header = "Selecionar tudo", Command = System.Windows.Input.ApplicationCommands.SelectAll, CommandTarget = this });
        ContextMenu = menu;
        System.Windows.Input.CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command != System.Windows.Input.ApplicationCommands.Copy) return;
            e.Handled = true;
            if (!Selection.IsEmpty) ToolTip = ClipboardCopy.TrySet(Selection.Text) ? "Trecho copiado" : "Não foi possível copiar. Tente novamente.";
        });
    }

    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(MarkdownTextBlock), new PropertyMetadata("", RenderChanged));
    public static readonly DependencyProperty RenderMarkdownProperty = DependencyProperty.Register(nameof(RenderMarkdown), typeof(bool), typeof(MarkdownTextBlock), new PropertyMetadata(true, RenderChanged));
    public string SourceText { get => (string)GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }
    public bool RenderMarkdown { get => (bool)GetValue(RenderMarkdownProperty); set => SetValue(RenderMarkdownProperty, value); }
    private static readonly Regex InlinePattern = new(@"(`[^`\n]+`|\\\([^\n]*?\\\)|\\\[[^\n]*?\\\]|\$\$[^\n]*?\$\$|\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*|\[[^\]\n]+\]\(https?://[^\s)]+\))", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-+*]\s+", RegexOptions.Compiled);
    private static void RenderChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((MarkdownTextBlock)target).Render();

    private void Render()
    {
        var start = Document.ContentStart.GetOffsetToPosition(Selection.Start);
        var end = Document.ContentStart.GetOffsetToPosition(Selection.End);
        var selected = !Selection.IsEmpty;
        RenderCore();
        if (selected)
        {
            var length = Document.ContentStart.GetOffsetToPosition(Document.ContentEnd);
            Selection.Select(Document.ContentStart.GetPositionAtOffset(Math.Min(start, length))!, Document.ContentStart.GetPositionAtOffset(Math.Min(end, length))!);
        }
    }
    private void RenderCore()
    {
        Inlines.Clear();
        var source = SourceText ?? "";
        if (!RenderMarkdown) { Inlines.Add(new Run(source)); return; }
        var code = false;
        string? fence = null;
        var first = true;
        System.Text.StringBuilder? formula = null;
        string? mathEnd = null;
        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if ((!code && (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))) || (code && trimmed.StartsWith(fence!)))
            {
                if (!code) { fence = trimmed.StartsWith("```") ? "```" : "~~~"; code = true; }
                else code = false;
                continue;
            }
            if (!code && formula is not null)
            {
                if (trimmed == mathEnd)
                {
                    if (!first) Inlines.Add(new LineBreak());
                    Inlines.Add(MathInline.Create(formula.ToString())); first = false; formula = null;
                }
                else formula.AppendLine(line);
                continue;
            }
            if (!code && (trimmed == @"\[" || trimmed == "$$"))
            {
                formula = new System.Text.StringBuilder(); mathEnd = trimmed == "$$" ? "$$" : @"\]";
                continue;
            }
            if (!first) Inlines.Add(new LineBreak());
            first = false;
            if (code)
            {
                var run = new Run(line) { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
                run.SetResourceReference(TextElement.BackgroundProperty, "ShellBrush");
                Inlines.Add(run);
                continue;
            }
            var heading = Heading.Match(line);
            if (heading.Success)
            {
                var title = new Span { FontWeight = FontWeights.SemiBold, FontSize = heading.Groups[1].Length <= 2 ? 18 : 15 };
                AddInline(title.Inlines, heading.Groups[2].Value);
                Inlines.Add(title);
            }
            else if (trimmed is "---" or "***" or "___")
            {
                var rule = new Run("────────────────────────") { FontSize = 11 };
                rule.SetResourceReference(TextElement.ForegroundProperty, "BorderBrush"); Inlines.Add(rule);
            }
            else
            {
                var text = Bullet.IsMatch(line) ? Bullet.Replace(line, "• ", 1) : line;
                if (trimmed.StartsWith("> "))
                {
                    var quote = new Span { FontStyle = FontStyles.Italic };
                    quote.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
                    AddInline(quote.Inlines, "│ " + trimmed[2..]); Inlines.Add(quote);
                }
                else AddInline(Inlines, text);
            }
        }
        if (formula is not null) { if (!first) Inlines.Add(new LineBreak()); Inlines.Add(MathInline.Create(formula.ToString())); }
    }

    private static void AddInline(InlineCollection target, string text)
    {
        MatchCollection matches;
        try { matches = InlinePattern.Matches(text); _ = matches.Count; }
        catch (RegexMatchTimeoutException) { target.Add(new Run(text)); return; }
        var offset = 0;
        foreach (Match match in matches)
        {
            if (match.Index > offset) target.Add(new Run(text[offset..match.Index]));
            var value = match.Value;
            if (value.StartsWith('`'))
            {
                var run = new Run(value[1..^1]) { FontFamily = new FontFamily("Consolas") };
                run.SetResourceReference(TextElement.BackgroundProperty, "ShellBrush"); target.Add(run);
            }
            else if (value.StartsWith(@"\(") || value.StartsWith(@"\[") || value.StartsWith("$$")) target.Add(MathInline.Create(value[2..^2]));
            else if (value.StartsWith("**") || value.StartsWith("__")) target.Add(new Bold(new Run(value[2..^2])));
            else if (value.StartsWith('*')) target.Add(new Italic(new Run(value[1..^1])));
            else
            {
                var separator = value.IndexOf("](", StringComparison.Ordinal);
                var link = new Run(value[1..separator]) { ToolTip = value[(separator + 2)..^1], TextDecorations = System.Windows.TextDecorations.Underline };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextBrush"); target.Add(link);
            }
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) target.Add(new Run(text[offset..]));
    }
}
