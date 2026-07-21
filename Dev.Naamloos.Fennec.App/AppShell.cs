using Dev.Naamloos.Fennec.App.Pages;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Dev.Naamloos.Fennec.App;

public sealed class AppShell : Shell
{
    public AppShell(
        ManagedMatrixClient matrixClient,
        IServiceProvider services)
    {
        SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "AppBackground");
        SetDynamicResource(
            FlyoutBackgroundColorProperty,
            "Surface");

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutWidth = 320;
        FlyoutContent = CreateFlyoutContent(matrixClient);

        var roomsItem = new FlyoutItem
        {
            Title = "Rooms",
            Route = "rooms",
            Items =
            {
                new ShellContent
                {
                    Title = "Rooms",
                    Route = "room-list",
                    ContentTemplate = new DataTemplate(
                        () => services.GetRequiredService<RoomList>()),
                },
            },
        };

        Items.Add(roomsItem);
        CurrentItem = roomsItem;
    }

    private static View CreateFlyoutContent(
        ManagedMatrixClient matrixClient)
    {
        var userIdLabel = new Label
        {
            Text = matrixClient.IsLoggedIn
                ? "Signed in"
                : "yeah idk TODO!",
            FontSize = 12,
            Opacity = 0.7,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        var header = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = "Fennec",
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                },
                userIdLabel,
            },
        };

        var layout = new Grid
        {
            Padding = 16,
            RowSpacing = 16,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };

        layout.Add(header, row: 0);
        return layout;
    }
}