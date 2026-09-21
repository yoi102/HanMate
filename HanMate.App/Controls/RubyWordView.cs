using System.Globalization;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.App.Controls;

/// <summary>Native short-word ruby: each token owns its pinyin and Hanzi in one wrapping cell.</summary>
public sealed class RubyWordView : FlexLayout
{
    public RubyWordView(TextUnit unit, PinyinExample? highlight = null, double scale = 1)
    {
        if (unit.Tokens.Count > 128) throw new ArgumentException("Use the long-text renderer for this unit.", nameof(unit));
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap;
        AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.End;
        foreach (var token in unit.Tokens)
        {
            var pinyin = new Label { FontSize = 19 * scale, HorizontalTextAlignment = TextAlignment.Center };
            var formatted = new FormattedString();
            if (token.Pinyin is { } syllable)
            {
                var offsets = StringInfo.ParseCombiningCharacters(syllable.Display).Append(syllable.Display.Length).ToArray();
                for (var i = 0; i < offsets.Length - 1; i++)
                {
                    var selected = highlight is not null && highlight.TokenId == token.Id && i >= highlight.PinyinStart && i < highlight.PinyinStart + highlight.PinyinLength;
                    var span = new Span { Text = syllable.Display[offsets[i]..offsets[i + 1]] };
                    if (selected) { span.TextColor = Color.FromArgb("#B3261E"); span.FontAttributes = FontAttributes.Bold; }
                    formatted.Spans.Add(span);
                }
            }
            pinyin.FormattedText = formatted;
            var hanzi = new Label { Text = token.Text, FontSize = 34 * scale, HorizontalTextAlignment = TextAlignment.Center };
            var cell = new VerticalStackLayout { Padding = new Thickness(7, 3), Spacing = 3, Children = { pinyin, hanzi } };
            SemanticProperties.SetDescription(cell, token.Text + " " + token.Pinyin?.Display);
            Children.Add(cell);
        }
    }
}
