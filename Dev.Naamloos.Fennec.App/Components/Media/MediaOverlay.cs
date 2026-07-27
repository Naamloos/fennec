using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MediaOverlay : ContentView
{
    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial ChatMedia? Media { get; set; }

    [BindableProperty]
    public partial ICommand? CloseCommand { get; set; }

    public MediaOverlay()
    {
        InputTransparent = false;
        Content = new Grid
        {
            BackgroundColor = Colors.Black,
            Children =
            {
                new MatrixMedia { IsFull = true }
                    .Bind(MatrixMedia.ClientProperty, nameof(Client), source: this)
                    .Bind(MatrixMedia.MediaProperty, nameof(Media), source: this),

                new Button
                {
                    Text = "×",
                    FontSize = 28,
                    WidthRequest = 48,
                    HeightRequest = 48,
                    Padding = 0,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Start,
                    ZIndex = 1,
                }
                .BindCommand(nameof(CloseCommand), source: this),
            },
        };
    }
}
