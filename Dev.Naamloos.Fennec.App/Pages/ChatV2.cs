using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class ChatV2 : ContentPage
{
	public ChatV2()
	{
        build();
	}

    [RelayCommand]
    public async Task SendAsync(string message)
    {

    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // TODO: proper unloading
        // discard timeline watcher
        // clear local messages
        // unset local room
        // bye bye
    }

    private void build()
    {
        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    EventName = nameof(Unloaded),
                }
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children =
            {
                new CollectionView
                {
                    //ItemsSource = some observable collection of messages
                },
                new ChatComposer
                {
                    //SendCommand = some command to send messages
                }
                .Bind(ChatComposer.SendCommandProperty, nameof(SendAsync))
            }
        };
    }
}
