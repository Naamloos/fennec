using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public enum ComposerAutocompleteMode
{
    None,
    Mentions,
    Emotes,
}

public sealed partial class ComposerAutocomplete : ContentView
{
    private INotifyCollectionChanged? _membersSource;
    private INotifyCollectionChanged? _emotesSource;

    public ObservableCollection<RoomMember> VisibleMembers { get; } = [];

    public ObservableCollection<MatrixEmote> VisibleEmotes { get; } = [];

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial IEnumerable<MatrixEmote>? Emotes { get; set; }

    [BindableProperty]
    public partial string Query { get; set; } = string.Empty;

    [BindableProperty]
    public partial ComposerAutocompleteMode Mode { get; set; }

    [BindableProperty]
    public partial ICommand? PickMemberCommand { get; set; }

    [BindableProperty]
    public partial ICommand? PickEmoteCommand { get; set; }

    public ComposerAutocomplete()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Members))
            {
                Subscribe(ref _membersSource, Members as INotifyCollectionChanged);
            }
            else if (args.PropertyName == nameof(Emotes))
            {
                Subscribe(ref _emotesSource, Emotes as INotifyCollectionChanged);
            }

            if (args.PropertyName is nameof(Members) or nameof(Emotes) or nameof(Query) or nameof(Mode))
            {
                Refresh();
            }
        };

        Content = CreateContent();
    }

    private void Subscribe(ref INotifyCollectionChanged? current, INotifyCollectionChanged? source)
    {
        if (ReferenceEquals(current, source))
        {
            return;
        }

        if (current is not null)
        {
            current.CollectionChanged -= OnSourceCollectionChanged;
        }

        current = source;
        if (current is not null)
        {
            current.CollectionChanged += OnSourceCollectionChanged;
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if ((ReferenceEquals(sender, _membersSource) && Mode == ComposerAutocompleteMode.Mentions) ||
            (ReferenceEquals(sender, _emotesSource) && Mode == ComposerAutocompleteMode.Emotes))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var query = Query.Trim();
        VisibleMembers.Clear();
        VisibleEmotes.Clear();

        if (Mode == ComposerAutocompleteMode.Mentions)
        {
            foreach (var member in Members?.Where(member =>
                         member.UserId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         (member.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                     .Take(8) ?? [])
            {
                VisibleMembers.Add(member);
            }
        }
        else if (Mode == ComposerAutocompleteMode.Emotes)
        {
            foreach (var emote in Emotes?.Where(emote =>
                         emote.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         emote.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .Take(8) ?? [])
            {
                VisibleEmotes.Add(emote);
            }
        }

        Content = CreateContent();
    }

    private View CreateContent()
    {
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            MaximumHeightRequest = 260,
            ItemTemplate = Mode == ComposerAutocompleteMode.Emotes
                ? new DataTemplate(() => new Button
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    Padding = new Thickness(8, 5),
                }
                .Bind(Button.TextProperty, nameof(MatrixEmote.Name), stringFormat: ":{0}:")
                .BindCommand(nameof(PickEmoteCommand), source: this)
                .Bind(Button.CommandParameterProperty, "."))
                : new DataTemplate(() => new Button
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    Padding = new Thickness(8, 5),
                }
                .Bind(Button.TextProperty, nameof(RoomMember.DisplayName))
                .BindCommand(nameof(PickMemberCommand), source: this)
                .Bind(Button.CommandParameterProperty, ".")),
        };
        list.ItemsSource = Mode == ComposerAutocompleteMode.Emotes ? VisibleEmotes : VisibleMembers;

        return new Border
        {
            MaximumWidthRequest = 360,
            Padding = new Thickness(6),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = list,
        }.DynamicResource(BackgroundColorProperty, "SurfaceContainer");
    }
}
