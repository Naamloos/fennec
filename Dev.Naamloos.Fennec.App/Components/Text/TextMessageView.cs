using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class TextMessageView : ContentView
{
    [BindableProperty(PropertyChangedMethodName = nameof(OnItemChanged))]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial ICommand? LinkCommand { get; set; }

    public TextMessageView()
    {
        Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { FontSize = 12, Opacity = .7 }
                    .Bind(
                        Label.TextProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.ReplyPreview)}",
                        source: this
                    )
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.ReplyPreview)}",
                        converter: new Dev.Naamloos.Fennec.App.Converters.NotNullConverter(),
                        source: this
                    )
                    .Invoke(label => label.GestureRecognizers.Add(
                        new TapGestureRecognizer()
                            .BindCommand(nameof(LinkCommand), source: this)
                            .Bind(
                                TapGestureRecognizer.CommandParameterProperty,
                                $"{nameof(Item)}.{nameof(ChatTimelineItem.ReplyToEventId)}",
                                source: this
                            )
                    )),
                new MatrixHtmlView()
                    .Bind(
                        MatrixHtmlView.HtmlProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.FormattedBody)}",
                        source: this
                    )
                    .Bind(
                        MatrixHtmlView.FallbackTextProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.Body)}",
                        source: this
                    )
                    .Bind(MatrixHtmlView.ClientProperty, nameof(Client), source: this)
                    .Bind(MatrixHtmlView.MembersProperty, nameof(Members), source: this)
                    .Bind(MatrixHtmlView.LinkCommandProperty, nameof(LinkCommand), source: this),
            },
        };
    }

    private static void OnItemChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TextMessageView)bindable).IsVisible = newValue is ChatTimelineItem { Poll: null };
}
