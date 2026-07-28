using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Collections.ObjectModel;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class EmoteSettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient), typeof(ManagedMatrixClient), typeof(EmoteSettingsView));

    public ObservableCollection<MatrixEmote> Emotes { get; } = [];

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public EmoteSettingsView()
    {
        this.BindService<ManagedMatrixClient, EmoteSettingsView>(MatrixClientProperty);
        Loaded += async (_, _) => await RefreshAsync();

        Content = new SettingsSection("Personal stickers and emoji",
            new Label { Text = "These are available in every chat.", Opacity = .7, FontSize = 12 },
            new Button { Text = "Add sticker or emoji", BackgroundColor = Colors.Transparent }
                .DynamicResource(Button.TextColorProperty, "Primary")
                .BindCommand(nameof(AddEmoteCommand), source: this),
            new CollectionView
            {
                HeightRequest = 180,
                SelectionMode = SelectionMode.None,
                ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical),
                ItemTemplate = new DataTemplate(() => new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new MatrixImage { IsJson = false, HeightRequest = 64, Aspect = Aspect.AspectFit }
                            .Bind(MatrixImage.MatrixSourceProperty, nameof(MatrixEmote.Source)),
                        new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation }
                            .Bind(Label.TextProperty, nameof(MatrixEmote.Name)),
                        new Button { Text = "Remove", FontSize = 11, TextColor = Colors.Red, BackgroundColor = Colors.Transparent, Padding = 0 }
                            .BindCommand(nameof(RemoveEmoteCommand), source: this)
                            .Bind(Button.CommandParameterProperty, "."),
                    },
                }),
            }.Bind(ItemsView.ItemsSourceProperty, nameof(Emotes), source: this));
    }

    private async Task RefreshAsync()
    {
        if (MatrixClient is null) return;

        var emotes = await MatrixClient.GetUserEmotesAsync();
        Emotes.Clear();
        foreach (var emote in emotes) Emotes.Add(emote);
    }

    [RelayCommand]
    private async Task AddEmoteAsync()
    {
        if (MatrixClient is null) return;

        var name = await Shell.Current.DisplayPromptAsync("Emoji name", "Use the name people type between colons.");
        if (string.IsNullOrWhiteSpace(name) || Emotes.Any(emote => emote.Name == name.Trim())) return;

        var attachment = await AttachmentPicker.PickConfirmedAsync(new PickOptions
        {
            PickerTitle = "Choose sticker or emoji",
            FileTypes = FilePickerFileType.Images,
        });
        if (attachment is null) return;

        var source = await MatrixClient.UploadMediaAsync(attachment.MimeType, attachment.Data);
        Emotes.Add(new MatrixEmote(name.Trim(), name.Trim(), source));
        await MatrixClient.SetUserEmotesAsync(Emotes);
    }

    [RelayCommand]
    private async Task RemoveEmoteAsync(MatrixEmote? emote)
    {
        if (emote is null || MatrixClient is null) return;

        Emotes.Remove(emote);
        await MatrixClient.SetUserEmotesAsync(Emotes);
    }
}
