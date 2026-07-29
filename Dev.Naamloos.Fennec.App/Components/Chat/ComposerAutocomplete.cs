using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public enum ComposerAutocompleteMode
{
    None,
    Mentions,
    Emotes,
    Rooms,
}

public sealed partial class ComposerAutocomplete : ContentView
{
    private INotifyCollectionChanged? _membersSource;
    private INotifyCollectionChanged? _emotesSource;
    private INotifyCollectionChanged? _roomsSource;

    public ObservableCollection<RoomMember> VisibleMembers { get; } = [];

    public ObservableCollection<MatrixEmote> VisibleEmotes { get; } = [];

    public ObservableCollection<ManagedRoom> VisibleRooms { get; } = [];

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial IEnumerable<MatrixEmote>? Emotes { get; set; }

    [BindableProperty]
    public partial IEnumerable<ManagedRoom>? Rooms { get; set; }

    [BindableProperty]
    public partial string Query { get; set; } = string.Empty;

    [BindableProperty]
    public partial ComposerAutocompleteMode Mode { get; set; }

    [BindableProperty]
    public partial ICommand? PickMemberCommand { get; set; }

    [BindableProperty]
    public partial ICommand? PickEmoteCommand { get; set; }

    [BindableProperty]
    public partial ICommand? PickRoomCommand { get; set; }

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
            else if (args.PropertyName == nameof(Rooms))
            {
                Subscribe(ref _roomsSource, Rooms as INotifyCollectionChanged);
            }

            if (
                args.PropertyName
                is nameof(Members)
                    or nameof(Emotes)
                    or nameof(Rooms)
                    or nameof(Query)
                    or nameof(Mode)
            )
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
        if (
            (ReferenceEquals(sender, _membersSource) && Mode == ComposerAutocompleteMode.Mentions)
            || (ReferenceEquals(sender, _emotesSource) && Mode == ComposerAutocompleteMode.Emotes)
            || (ReferenceEquals(sender, _roomsSource) && Mode == ComposerAutocompleteMode.Rooms)
        )
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var query = Query.Trim();
        VisibleMembers.Clear();
        VisibleEmotes.Clear();
        VisibleRooms.Clear();

        if (Mode == ComposerAutocompleteMode.Mentions)
        {
            foreach (
                var member in Members
                    ?.Where(member =>
                        member.UserId.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (
                            member.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase)
                            ?? false
                        )
                    )
                    .Take(8)
                    ?? []
            )
            {
                VisibleMembers.Add(member);
            }
        }
        else if (Mode == ComposerAutocompleteMode.Emotes)
        {
            foreach (
                var emote in Emotes
                    ?.Where(emote =>
                        emote.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || emote.Body.Contains(query, StringComparison.OrdinalIgnoreCase)
                    )
                    .Take(8)
                    ?? []
            )
            {
                VisibleEmotes.Add(emote);
            }
        }
        else if (Mode == ComposerAutocompleteMode.Rooms)
        {
            foreach (
                var room in Rooms
                    ?.Where(room =>
                        !room.IsSpace
                        && (
                            (
                                room.DisplayName?.Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase
                                ) ?? false
                            )
                            || (
                                room.Id?.Contains(query, StringComparison.OrdinalIgnoreCase)
                                ?? false
                            )
                        )
                    )
                    .Take(8)
                    ?? []
            )
            {
                VisibleRooms.Add(room);
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
            ItemTemplate = Mode switch
            {
                ComposerAutocompleteMode.Emotes => CreateEmoteTemplate(),
                ComposerAutocompleteMode.Rooms => CreateRoomTemplate(),
                _ => CreateMemberTemplate(),
            },
        };
        list.ItemsSource = Mode switch
        {
            ComposerAutocompleteMode.Emotes => VisibleEmotes,
            ComposerAutocompleteMode.Rooms => VisibleRooms,
            _ => VisibleMembers,
        };

        return new Border
        {
            MaximumWidthRequest = 360,
            Padding = new Thickness(6),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = list,
        }.DynamicResource(BackgroundColorProperty, "SurfaceContainer");
    }

    private DataTemplate CreateMemberTemplate() =>
        new(() =>
            CreateRow(
                new MatrixAvatar { Size = 34 }
                    .Bind(MatrixAvatar.MatrixSourceProperty, nameof(RoomMember.AvatarUrl))
                    .Bind(MatrixAvatar.DisplayNameProperty, nameof(RoomMember.DisplayName)),
                new VerticalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        new Label { FontAttributes = FontAttributes.Bold }.Bind(
                            Label.TextProperty,
                            nameof(RoomMember.DisplayName)
                        ),
                        new Label { FontSize = 11, Opacity = .68 }.Bind(
                            Label.TextProperty,
                            nameof(RoomMember.UserId)
                        ),
                    },
                },
                nameof(PickMemberCommand)
            )
        );

    private DataTemplate CreateEmoteTemplate() =>
        new(() =>
            CreateRow(
                new MatrixImage
                {
                    IsJson = false,
                    WidthRequest = 34,
                    HeightRequest = 34,
                    Aspect = Aspect.AspectFit,
                }.Bind(MatrixImage.MatrixSourceProperty, nameof(MatrixEmote.Source)),
                new VerticalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        new Label { FontAttributes = FontAttributes.Bold }.Bind(
                            Label.TextProperty,
                            nameof(MatrixEmote.Name),
                            stringFormat: ":{0}:"
                        ),
                        new Label { FontSize = 11, Opacity = .68 }.Bind(
                            Label.TextProperty,
                            nameof(MatrixEmote.Body)
                        ),
                    },
                },
                nameof(PickEmoteCommand)
            )
        );

    private DataTemplate CreateRoomTemplate() =>
        new(() =>
            CreateRow(
                new MatrixAvatar { Size = 34 }
                    .Bind(MatrixAvatar.MatrixSourceProperty, nameof(ManagedRoom.AvatarUrl))
                    .Bind(MatrixAvatar.DisplayNameProperty, nameof(ManagedRoom.DisplayName)),
                new VerticalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        new Label { FontAttributes = FontAttributes.Bold }.Bind(
                            Label.TextProperty,
                            nameof(ManagedRoom.DisplayName)
                        ),
                        new Label { FontSize = 11, Opacity = .68 }.Bind(
                            Label.TextProperty,
                            nameof(ManagedRoom.Id)
                        ),
                    },
                },
                nameof(PickRoomCommand)
            )
        );

    private View CreateRow(View icon, View text, string commandName) =>
        new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Children = { icon.Column(0), text.Column(1) },
            GestureRecognizers =
            {
                new TapGestureRecognizer()
                    .BindCommand(commandName, source: this)
                    .Bind(TapGestureRecognizer.CommandParameterProperty, "."),
            },
        };
}
