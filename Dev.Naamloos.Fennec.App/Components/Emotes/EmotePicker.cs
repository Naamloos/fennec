using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class EmotePicker : ContentView
{
    private static readonly EmojiCategory[] Categories =
    [
        new("Smileys", "😀", [new(0x1F600, 0x1F64F)]),
        new("People", "🧑", [new(0x1F466, 0x1F487), new(0x1F90C, 0x1F9DD)]),
        new("Animals", "🐶", [new(0x1F400, 0x1F43F), new(0x1F980, 0x1F9A2)]),
        new("Food", "🍔", [new(0x1F32D, 0x1F37F), new(0x1F950, 0x1F96F)]),
        new("Travel", "🚗", [new(0x1F680, 0x1F6FF), new(0x1F3D4, 0x1F3F0)]),
        new("Activities", "⚽", [new(0x1F3A0, 0x1F3CF), new(0x1F93A, 0x1F94C)]),
        new("Objects", "💡", [new(0x1F4A0, 0x1F4FF), new(0x1F9E0, 0x1F9FF)]),
        new("Symbols", "❤️", [new(0x2600, 0x26FF), new(0x2700, 0x27BF), new(0x1FA00, 0x1FAFF)]),
    ];

    public ObservableCollection<string> VisibleEmotes { get; } = [];

    public IReadOnlyList<EmojiCategory> EmojiCategories => Categories;

    [BindableProperty]
    public partial IEnumerable<MatrixEmote>? Emotes { get; set; }

    [BindableProperty]
    public partial ICommand? PickCommand { get; set; }

    public EmotePicker()
    {
        SelectCategory(Categories[0]);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new CollectionView
                {
                    HeightRequest = 46,
                    SelectionMode = SelectionMode.None,
                    ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
                    {
                        ItemSpacing = 4,
                    },
                    ItemTemplate = new DataTemplate(() =>
                        new Button { Padding = new Thickness(8, 0), FontSize = 20 }
                            .Bind(Button.TextProperty, nameof(EmojiCategory.Icon))
                            .BindCommand(nameof(SelectCategoryCommand), source: this)
                            .Bind(Button.CommandParameterProperty, ".")
                    ),
                }
                    .Bind(ItemsView.ItemsSourceProperty, nameof(EmojiCategories), source: this)
                    .Row(0),
                new CollectionView
                {
                    SelectionMode = SelectionMode.None,
                    ItemsLayout = new GridItemsLayout(8, ItemsLayoutOrientation.Vertical)
                    {
                        HorizontalItemSpacing = 2,
                        VerticalItemSpacing = 2,
                    },
                    ItemTemplate = new DataTemplate(() =>
                        new Button { FontSize = 22, Padding = 0 }
                            .Bind(Button.TextProperty, ".")
                            .BindCommand(nameof(PickCommand), source: this)
                            .Bind(Button.CommandParameterProperty, ".")
                    ),
                }
                    .Bind(ItemsView.ItemsSourceProperty, nameof(VisibleEmotes), source: this)
                    .Row(1),
                new CollectionView
                {
                    HeightRequest = 92,
                    SelectionMode = SelectionMode.None,
                    ItemsLayout = new GridItemsLayout(8, ItemsLayoutOrientation.Vertical),
                    ItemTemplate = new DataTemplate(() =>
                        new MatrixImage
                        {
                            IsJson = false,
                            Aspect = Aspect.AspectFit,
                            WidthRequest = 38,
                            HeightRequest = 38,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer()
                                    .BindCommand(nameof(PickCommand), source: this)
                                    .Bind(
                                        TapGestureRecognizer.CommandParameterProperty,
                                        nameof(MatrixEmote.Body)
                                    ),
                            },
                        }.Bind(MatrixImage.MatrixSourceProperty, nameof(MatrixEmote.Source))
                    ),
                }
                    .Bind(ItemsView.ItemsSourceProperty, nameof(Emotes), source: this)
                    .Bind(
                        IsVisibleProperty,
                        nameof(Emotes),
                        converter: new Dev.Naamloos.Fennec.App.Converters.NotNullConverter(),
                        source: this
                    )
                    .Row(2),
            },
        };
    }

    [RelayCommand]
    private void SelectCategory(EmojiCategory? category)
    {
        if (category is null)
        {
            return;
        }

        VisibleEmotes.Clear();

        foreach (var emoji in EmojiCatalog.Create(category.Ranges))
        {
            VisibleEmotes.Add(emoji);
        }
    }

    public sealed record EmojiCategory(
        string Name,
        string Icon,
        IReadOnlyList<EmojiCatalog.UnicodeRange> Ranges
    );
}
