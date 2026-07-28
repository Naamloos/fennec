using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using MauiIcons.Core;
using MauiIcons.Material;
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
            BackgroundColor = new Color(0,0,0, 200),
            Children =
            {
                new MatrixMedia { IsFull = true }
                    .Bind(MatrixMedia.ClientProperty, nameof(Client), source: this)
                    .Bind(MatrixMedia.MediaProperty, nameof(Media), source: this),

                new MauiIcon
                {
                    Icon = MaterialIcons.Close,
                    IconSize = 28,
                    IconColor = Colors.White,
                    WidthRequest = 48,
                    HeightRequest = 48,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Start,
                    ZIndex = 1,
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer()
                            .BindCommand(nameof(CloseCommand), source: this),
                    },
                },
            },
        };
    }
}
