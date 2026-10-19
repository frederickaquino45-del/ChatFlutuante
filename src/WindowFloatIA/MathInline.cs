using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowFloatIA;

internal static class MathInline
{
    internal static Span Create(string source)
    {
        try
        {
            var parser = new Parser(source.Trim().Replace("\r", " ").Replace("\n", " "));
            return parser.Read();
        }
        catch (FormatException)
        {
            // Preserve unsupported or incomplete expressions rather than changing their meaning.
            return new Span(new Run(source)) { FontFamily = new FontFamily("Cambria Math"), ToolTip = "Expressão LaTeX original" };
        }
    }

    private sealed class Parser(string text)
    {
        private int _position;
        private int _depth;
        private static readonly Dictionary<string, string> Symbols = new()
        {
            ["Delta"] = "Δ", ["delta"] = "δ", ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ",
            ["theta"] = "θ", ["pi"] = "π", ["sigma"] = "σ", ["lambda"] = "λ", ["omega"] = "ω",
            ["pm"] = "±", ["mp"] = "∓", ["neq"] = "≠", ["ne"] = "≠", ["leq"] = "≤", ["geq"] = "≥",
            ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["infty"] = "∞", ["approx"] = "≈",
            ["to"] = "→", ["in"] = "∈", ["sum"] = "∑", ["int"] = "∫"
        };
        public Span Read(bool group = false)
        {
            if (++_depth > 32) throw new FormatException();
            var span = new Span { FontFamily = new FontFamily("Cambria Math"), FontSize = 16 };
            while (_position < text.Length)
            {
                var c = text[_position++];
                if (c == '}')
                {
                    if (!group) throw new FormatException();
                    _depth--; return span;
                }
                if (c == '{') span.Inlines.Add(Read(true));
                else if (c is '^' or '_')
                {
                    var script = Argument();
                    script.FontSize = 11;
                    script.BaselineAlignment = c == '^' ? BaselineAlignment.Superscript : BaselineAlignment.Subscript;
                    span.Inlines.Add(script);
                }
                else if (c == '\\')
                {
                    var start = _position;
                    while (_position < text.Length && char.IsLetter(text[_position])) _position++;
                    var command = text[start.._position];
                    if (Symbols.TryGetValue(command, out var symbol)) span.Inlines.Add(new Run(symbol));
                    else if (command is "frac" or "dfrac" or "tfrac")
                    {
                        var numerator = Argument(); var denominator = Argument();
                        var grid = new Grid { Margin = new Thickness(3, 2, 3, 2) };
                        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
                        var upper = Block(numerator); var lower = Block(denominator);
                        var border = new Border { Child = upper, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(3, 0, 3, 2) };
                        border.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
                        Grid.SetRow(lower, 1); lower.Margin = new Thickness(3, 2, 3, 0);
                        grid.Children.Add(border); grid.Children.Add(lower);
                        span.Inlines.Add(new InlineUIContainer(grid) { BaselineAlignment = BaselineAlignment.Center });
                    }
                    else if (command == "sqrt")
                    {
                        span.Inlines.Add(new Run("√(")); span.Inlines.Add(Argument()); span.Inlines.Add(new Run(")"));
                    }
                    else if (command is "text" or "mathrm" or "mathbf")
                    {
                        var content = Argument();
                        if (command == "mathbf") content.FontWeight = FontWeights.Bold;
                        span.Inlines.Add(content);
                    }
                    else if (command is "left" or "right") { }
                    else if (command.Length == 0 && _position < text.Length && ",;! ".Contains(text[_position]))
                    { _position++; span.Inlines.Add(new Run(" ")); }
                    else throw new FormatException();
                }
                else span.Inlines.Add(new Run(c.ToString()));
            }
            if (group) throw new FormatException();
            _depth--; return span;
        }
        private Span Argument()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++;
            if (_position >= text.Length) throw new FormatException();
            if (text[_position++] == '{') return Read(true);
            if (text[_position - 1] == '\\') throw new FormatException();
            return new Span(new Run(text[_position - 1].ToString()));
        }
        private static TextBlock Block(Span span)
        {
            var block = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            block.Inlines.Add(span); return block;
        }
    }
}
