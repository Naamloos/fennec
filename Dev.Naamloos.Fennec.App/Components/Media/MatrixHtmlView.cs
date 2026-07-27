using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using uniffi.matrix_sdk_ffi;
using Microsoft.Maui.Layouts;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MatrixHtmlView : ContentView
{
    private INotifyCollectionChanged? _membersSource;
    [BindableProperty]
    public partial string? Html { get; set; }

    [BindableProperty]
    public partial string FallbackText { get; set; } = string.Empty;

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial ICommand? LinkCommand { get; set; }

    public MatrixHtmlView()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Members))
            {
                if (_membersSource is not null)
                {
                    _membersSource.CollectionChanged -= OnMembersChanged;
                }

                _membersSource = Members as INotifyCollectionChanged;
                if (_membersSource is not null)
                {
                    _membersSource.CollectionChanged += OnMembersChanged;
                }
            }

            if (args.PropertyName is nameof(Html) or nameof(FallbackText) or nameof(Client) or nameof(Members))
            {
                Build();
            }
        };

        Build();
    }

    private void Build()
    {
        var layout = new VerticalStackLayout { Spacing = 3 };
        var html = Html;

        if (string.IsNullOrWhiteSpace(html))
        {
            layout.Add(Text(FallbackText, default));
            Content = layout;
            return;
        }

        try
        {
            var document = XDocument.Parse($"<root>{NormalizeHtml(html)}</root>",
                LoadOptions.PreserveWhitespace);
            AddBlocks(layout, document.Root!.Nodes());
        }
        catch
        {
            layout.Add(Text(FallbackText, default));
        }

        Content = layout;
    }

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems?.OfType<RoomMember>().Any(member =>
                Html?.Contains(member.UserId, StringComparison.Ordinal) == true) == true)
        {
            Build();
        }
    }

    private void AddBlocks(VerticalStackLayout layout, IEnumerable<XNode> nodes)
    {
        var inline = new List<XNode>();

        void FlushInline()
        {
            if (inline.Count == 0)
            {
                return;
            }

            var row = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                AlignItems = FlexAlignItems.Center,
            };
            AddInline(row, inline, default);
            layout.Add(row);
            inline.Clear();
        }

        foreach (var node in nodes)
        {
            if (node is not XElement element || !IsBlock(element.Name.LocalName))
            {
                inline.Add(node);
                continue;
            }

            FlushInline();
            AddBlock(layout, element);
        }

        FlushInline();
    }

    private void AddBlock(VerticalStackLayout layout, XElement element)
    {
        var name = element.Name.LocalName.ToLowerInvariant();
        if (name == "mx-reply")
        {
            return;
        }

        if (name == "hr")
        {
            layout.Add(new BoxView { HeightRequest = 1, Opacity = .35, Margin = new Thickness(0, 4) });
            return;
        }

        if (name is "ul" or "ol")
        {
            var index = 1;
            foreach (var item in element.Elements().Where(child => child.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
            {
                var listRow = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                    ColumnSpacing = 5,
                    Margin = new Thickness(0, 1),
                };
                listRow.Add(new Label { Text = name == "ol" ? $"{index++}." : "•" }.Column(0));
                var content = new VerticalStackLayout { Spacing = 2 };
                AddBlocks(content, item.Nodes());
                listRow.Add(content.Column(1));
                layout.Add(listRow);
            }
            return;
        }

        if (name == "blockquote")
        {
            var quote = new VerticalStackLayout { Spacing = 2 };
            AddBlocks(quote, element.Nodes());
            layout.Add(new Border
            {
                Padding = new Thickness(8, 3),
                StrokeThickness = 1,
                Opacity = .8,
                Content = quote,
            });
            return;
        }

        if (name == "pre")
        {
            layout.Add(new Label
            {
                Text = element.Value,
                FontFamily = "Courier New",
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(6, 3),
            });
            return;
        }

        var row = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
        };
        AddInline(row, element.Nodes(), name switch
        {
            "h1" => new InlineStyle(Bold: true, Size: 24),
            "h2" => new InlineStyle(Bold: true, Size: 20),
            "h3" => new InlineStyle(Bold: true, Size: 17),
            "h4" or "h5" or "h6" => new InlineStyle(Bold: true, Size: 15),
            _ => default,
        });
        layout.Add(row);
    }

    private void AddInline(FlexLayout layout, IEnumerable<XNode> nodes, InlineStyle style)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text when text.Value.Length > 0:
                    layout.Add(Text(text.Value, style));
                    break;
                case XElement element:
                    AddElement(layout, element, style);
                    break;
            }
        }
    }

    private void AddElement(FlexLayout layout, XElement element, InlineStyle style)
    {
        var name = element.Name.LocalName.ToLowerInvariant();
        if (name == "br")
        {
            layout.Add(Text("\n", style));
            return;
        }

        if (name == "img")
        {
            var source = element.Attribute("src")?.Value;
            if (source?.StartsWith("mxc://", StringComparison.OrdinalIgnoreCase) == true)
            {
                layout.Add(new MatrixImage
                {
                    Client = Client,
                    MatrixSource = source,
                    WidthRequest = 28,
                    HeightRequest = 28,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(1, 0),
                });
            }
            else
            {
                layout.Add(Text(element.Attribute("alt")?.Value ?? string.Empty, style));
            }
            return;
        }

        if (name == "a" && element.Attribute("href")?.Value is { } href &&
            TryMatrixMention(href, out var userId))
        {
            layout.Add(MentionBadge(userId));
            return;
        }

        if (name is "mx-reply" or "script" or "style")
        {
            return;
        }

        var next = name switch
        {
            "b" or "strong" => style with { Bold = true },
            "i" or "em" => style with { Italic = true },
            "s" or "del" or "strike" => style with { Strike = true },
            "code" => style with { Code = true },
            "a" => style with
            {
                Link = element.Attribute("href")?.Value,
                Mention = element.Attribute("href")?.Value?.Contains("matrix.to/#/@", StringComparison.OrdinalIgnoreCase) == true,
            },
            _ => style,
        };
        AddInline(layout, element.Nodes(), next);
    }

    private Label Text(string value, InlineStyle style)
    {
        var label = new Label
        {
            Text = value,
            FontAttributes = style.Bold && style.Italic
                ? FontAttributes.Bold | FontAttributes.Italic
                : style.Bold ? FontAttributes.Bold : style.Italic ? FontAttributes.Italic : FontAttributes.None,
            FontFamily = style.Code ? "Courier New" : null,
            TextDecorations = style.Strike ? TextDecorations.Strikethrough : TextDecorations.None,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        if (style.Size > 0)
        {
            label.FontSize = style.Size;
        }
        if (style.Mention)
        {
            label.TextColor = Colors.MediumPurple;
        }
        else if (style.Link is not null)
        {
            label.TextColor = Colors.DodgerBlue;
        }

        if (style is { Mention: false, Link: { } link })
        {
            label.GestureRecognizers.Add(new TapGestureRecognizer
            {
                CommandParameter = link,
            }
            .BindCommand(nameof(LinkCommand), source: this));
        }

        return label;
    }

    private static bool IsBlock(string name) => name.ToLowerInvariant() is
        "p" or "div" or "blockquote" or "pre" or "ul" or "ol" or "li" or "hr" or
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "mx-reply";

    private static string NormalizeHtml(string html) => VoidTag.Replace(
        html.Replace("&nbsp;", "&#160;"), "<$1$2 />");

    private static readonly Regex VoidTag = new(
        @"<(br|hr|img)(\s[^>]*?)?(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private View MentionBadge(string userId)
    {
        var member = Members?.FirstOrDefault(candidate => candidate.UserId == userId);
        return new Border
        {
            Padding = new Thickness(4, 1),
            Margin = new Thickness(1, 0),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new MatrixAvatar
                    {
                        MatrixSource = member?.AvatarUrl,
                        Size = 18,
                    },
                    new Label
                    {
                        Text = member?.DisplayName ?? userId,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.MediumPurple,
                    },
                },
            },
        }.DynamicResource(BackgroundColorProperty, "SurfaceContainerHighest");
    }

    private static bool TryMatrixMention(string href, out string userId)
    {
        const string marker = "matrix.to/#/";
        var index = href.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        userId = index < 0 ? string.Empty : Uri.UnescapeDataString(href[(index + marker.Length)..]);
        return userId.StartsWith('@');
    }

    private readonly record struct InlineStyle(
        bool Bold = false,
        bool Italic = false,
        bool Strike = false,
        bool Code = false,
        bool Mention = false,
        string? Link = null,
        double Size = 0);
}
