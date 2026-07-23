using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class RoomListPage : ContentPage
{
    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    public RoomListPage()
    {
        BindingContext = this;
        Title = "Rooms";
        build();
    }

    private void build()
    {
        Content = new Components.RoomList()
            .Bind(
                Components.RoomList.MatrixClientProperty,
                nameof(MatrixClient),
                source: BindingContext);
    }
}
