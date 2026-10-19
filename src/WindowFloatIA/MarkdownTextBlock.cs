using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowFloatIA;

// A native, text-only Markdown view: no browser, HTML execution or remote images.
public sealed class MarkdownTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(MarkdownTextBlock), new PropertyMetadata("", RenderChanged));
    public static readonly DependencyProperty RenderMarkdownProperty = DependencyProperty.Register(nameof(RenderMarkdown), typeof(bool), typeof(MarkdownTextBlock), new PropertyMetadata(true, RenderChanged));
    public string SourceText { get => (string)GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }
    public bool RenderMarkdown { get => (bool)GetValue(RenderMarkdownProperty); set => SetValue(RenderMarkdownProperty, value); }
    private static readonly Regex InlinePattern = new(@"(`[^`\n]+`|\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*|\[[^\]\n]+\]\(https?://[^\s)]+\))", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-+*]\s+", RegexOptions.Compiled);
    private static void RenderChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((MarkdownTextBlock)target).Render();

    private void Render()
    {
        Inlines.Clear();
        var source = SourceText ?? "";
        if (!RenderMarkdown) { Inlines.Add(new Run(source)); return; }
        var code = false;
        string? fence = null;
        var first = true;
        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if ((!code && (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))) || (code && trimmed.StartsWith(fence!)))
            {
                if (!code) { fence = trimmed.StartsWith("```") ? "```" : "~~~"; code = true; }
                else code = false;
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
