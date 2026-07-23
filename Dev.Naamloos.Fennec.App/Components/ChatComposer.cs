using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.App.Converters;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatComposer : ContentView
{
    [BindableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [BindableProperty]
    public partial bool HasError { get; set; }

    [BindableProperty]
    public partial ChatMessage? ReplyTo { get; set; }

    [BindableProperty]
    public partial ICommand? AttachCommand { get; set; }

    [BindableProperty]
    public partial ICommand? SendCommand { get; set; }

    [BindableProperty]
    public partial string Text { get; set; } = string.Empty;

    public ChatComposer()
    {
        Build();
    }

    private void Build()
    {
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Children =
            {
                new Label
                {
                    TextColor = Colors.Red,
                    Margin = new Thickness(12, 4),
                    LineBreakMode = LineBreakMode.WordWrap
                }
                .Bind(
                    Label.TextProperty,
                    nameof(ErrorMessage),
                    source: this
                )
                .Bind(
                    IsVisibleProperty,
                    nameof(HasError),
                    source: this
                )
                .Row(0),

                new Label
                {
                    Margin = new Thickness(12, 0),
                    FontSize = 12,
                    Opacity = .75,
                    IsVisible = false
                }
                .Bind(Label.TextProperty, $"{nameof(ReplyTo)}.{nameof(ChatMessage.Username)}",
                    stringFormat: "Replying to {0}",
                    source: this
                )
                .Bind(
                    IsVisibleProperty,
                    $"{nameof(ReplyTo)}",
                    converter: new NotNullConverter(),
                    source: this
                )
                .Row(1),

                new Grid
                {
                    Padding = 12,
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    Children =
                    {
                        new Button
                        {
                            Text = "+",
                            FontSize = 24,
                            WidthRequest = 40,
                            HeightRequest = 40,
                            CornerRadius = 999,
                            Padding = 0,
                            VerticalOptions = LayoutOptions.Center,
                        }
                        .BindCommand(
                            nameof(AttachCommand),
                            source: this
                        )
                        .Invoke(b => SemanticProperties.SetDescription(b, "Attach a file"))
                        .Column(0),
                        new Entry
                        {
                            Placeholder = "Message",
                            ReturnType = ReturnType.Send,
                            VerticalOptions = LayoutOptions.Center
                        }
                        .Bind(
                            Entry.TextProperty,
                            nameof(Text),
                            mode: BindingMode.TwoWay,
                            source: this
                        )
                        .Bind(
                            Entry.ReturnCommandProperty,
                            nameof(SendCommand),
                            source: this
                        )
                        .Column(1),
                        new Button
                        {
                            Text = "Send",
                            VerticalOptions = LayoutOptions.Center
                        }
                        .BindCommand(
                            nameof(SendCommand),
                            source: this
                        )
                        .Column(2)
                    }
                }.Row(2)
            }
        };
    }
}
