using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Services;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Components;

public enum EmojiPickerMode { Reaction, Composer }
public enum EmojiKind { Unicode, MatrixCustom }

public sealed record EmojiItem
{
    public required string Id { get; init; }
    public required EmojiKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Shortcode { get; init; }
    public required string CategoryId { get; init; }
    public required string SearchKey { get; init; }
    public string? Unicode { get; init; }
    public string? MxcUri { get; init; }
    public string? PackId { get; init; }
    public IReadOnlyList<EmojiItem> Variants { get; init; } = [];
}

public sealed record EmojiSelection
{
    public required EmojiKind Kind { get; init; }
    public required string Id { get; init; }
    public string? Unicode { get; init; }
    public string? MxcUri { get; init; }
    public string? Shortcode { get; init; }
}

public sealed record EmojiCategory(string Id, string Name, string Icon);

/// <summary>Virtualized, mode-aware picker. Unicode data is local; Matrix metadata is opt-in.</summary>
public sealed partial class EmojiPicker : ContentView
{
    private const int ResultLimit = 240;
    private readonly EmojiUsageService _usage;
    private readonly CollectionView _grid;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _customLoadCancellation;
    private IReadOnlyList<EmojiItem> _unicodeItems = [];
    private IReadOnlyList<EmojiItem> _customItems = [];

    public ObservableCollection<EmojiCategory> Categories { get; } = [];

    [BindableProperty(PropertyChangedMethodName = nameof(OnModeChanged))]
    public partial EmojiPickerMode Mode { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSessionChanged))]
    public partial ChatSession? Session { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnIsOpenChanged))]
    public partial bool IsOpen { get; set; }

    [BindableProperty]
    public partial ICommand? SelectedCommand { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSearchTextChanged))]
    public partial string SearchText { get; set; } = string.Empty;

    [BindableProperty(PropertyChangedMethodName = nameof(OnSelectedCategoryChanged))]
    public partial EmojiCategory? SelectedCategory { get; set; }

    [BindableProperty]
    public partial IReadOnlyList<EmojiItem> VisibleItems { get; set; } = [];

    [BindableProperty]
    public partial bool IsLoadingCustomEmotes { get; set; }

    [BindableProperty]
    public partial string? EmojiFontFamily { get; set; }

    public EmojiPicker()
    {
        _usage = App.Services.GetRequiredService<EmojiUsageService>();
        SetBinding(EmojiFontFamilyProperty, new Binding(
            nameof(UserSettingsService.SelectedEmojiFontFamily),
            source: App.Services.GetRequiredService<UserSettingsService>()));
        var categories = new CollectionView
        {
            HeightRequest = 46,
            SelectionMode = SelectionMode.Single,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 4 },
            ItemTemplate = new DataTemplate(() => new Label
            {
                Padding = new Thickness(10, 8), FontSize = 20, VerticalTextAlignment = TextAlignment.Center,
            }.Bind(Label.TextProperty, nameof(EmojiCategory.Icon)).Bind(Label.FontFamilyProperty, nameof(EmojiFontFamily), source: this).Bind(SemanticProperties.DescriptionProperty, nameof(EmojiCategory.Name))),
        };
        categories.SelectionChanged += (_, args) => SelectedCategory = args.CurrentSelection.FirstOrDefault() as EmojiCategory;
        categories.SetBinding(ItemsView.ItemsSourceProperty, new Binding(nameof(Categories), source: this));
        categories.SetBinding(SelectableItemsView.SelectedItemProperty, new Binding(nameof(SelectedCategory), source: this));

        _grid = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            EmptyView = new Label
            {
                Text = "No emoji found.",
                Opacity = .7,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
            ItemsLayout = new GridItemsLayout(8, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 2, VerticalItemSpacing = 2,
            },
            ItemTemplate = new EmojiItemTemplateSelector(this),
        };
        _grid.SelectionChanged += OnEmojiSelected;
        _grid.SetBinding(ItemsView.ItemsSourceProperty, new Binding(nameof(VisibleItems), source: this));

        Content = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            Children =
            {
                new SearchBar { Placeholder = "Search emoji" }
                    .Bind(SearchBar.TextProperty, nameof(SearchText), BindingMode.TwoWay, source: this)
                    .Bind(SearchBar.FontFamilyProperty, nameof(EmojiFontFamily), source: this).Row(0),
                categories.Row(1),
                _grid.Row(2),
                new ActivityIndicator { HeightRequest = 28, IsRunning = true }
                    .Bind(IsVisibleProperty, nameof(IsLoadingCustomEmotes), source: this).Row(3),
            },
        };
        _ = ConfigureCategoriesAsync();
        _ = LoadUnicodeAsync();
        SizeChanged += (_, _) => UpdateGridSpan();
        Unloaded += (_, _) =>
        {
            _searchCancellation?.Cancel();
            _customLoadCancellation?.Cancel();
        };
    }

    private static void OnModeChanged(BindableObject bindable, object oldValue, object newValue) => _ = ((EmojiPicker)bindable).ConfigureCategoriesAsync();
    private static void OnSessionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var picker = (EmojiPicker)bindable;
        if (picker.Mode == EmojiPickerMode.Composer && picker.IsOpen)
            _ = picker.LoadCustomEmotesAsync();
    }
    private static void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var picker = (EmojiPicker)bindable;
        if (picker.IsOpen && picker.Mode == EmojiPickerMode.Composer)
            _ = picker.LoadCustomEmotesAsync();
    }
    private static void OnSelectedCategoryChanged(BindableObject bindable, object oldValue, object newValue) => ((EmojiPicker)bindable).ScheduleFilter();
    private static void OnSearchTextChanged(BindableObject bindable, object oldValue, object newValue) => ((EmojiPicker)bindable).ScheduleFilter();

    private async Task ConfigureCategoriesAsync()
    {
        await _usage.RefreshAsync();
        var unicodeCategories = await EmojiCatalog.GetCategoriesAsync();
        Categories.Clear();
        if (Mode == EmojiPickerMode.Composer)
            Categories.Add(new("custom", "Custom emotes", "✨"));
        Categories.Add(new("recent", "Recent", "🕘"));
        Categories.Add(new("favourites", "Favourites", "★"));
        foreach (var category in unicodeCategories)
            Categories.Add(category);
        SelectedCategory = Categories.FirstOrDefault();
        if (Mode == EmojiPickerMode.Composer && IsOpen)
            _ = LoadCustomEmotesAsync();
    }

    private async Task LoadCustomEmotesAsync()
    {
        if (Session is null || IsLoadingCustomEmotes)
            return;
        _customLoadCancellation?.Cancel();
        _customLoadCancellation?.Dispose();
        _customLoadCancellation = new CancellationTokenSource();
        IsLoadingCustomEmotes = true;
        try
        {
            await Session.LoadCustomEmotesAsync(_customLoadCancellation.Token);
            _customLoadCancellation.Token.ThrowIfCancellationRequested();
            _customItems = Session.Emotes.Select(CreateCustomItem).ToArray();
            ScheduleFilter();
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsLoadingCustomEmotes = false;
        }
    }

    private async Task LoadUnicodeAsync()
    {
        try
        {
            _unicodeItems = await EmojiCatalog.GetItemsAsync();
            ScheduleFilter();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load bundled Unicode emoji: {exception}");
        }
    }

    private void UpdateGridSpan()
    {
        var span = Math.Max(1, (int)(Math.Max(Width, 46) / 48));
        if (_grid.ItemsLayout is not GridItemsLayout { Span: var current } || current != span)
        {
            _grid.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 2, VerticalItemSpacing = 2,
            };
        }
    }

    private static EmojiItem CreateCustomItem(MatrixEmote emote)
    {
        var pack = emote.PackId ?? "personal";
        var name = string.IsNullOrWhiteSpace(emote.Body) ? emote.Name : emote.Body;
        return new EmojiItem
        {
            Id = $"matrix:{pack}:{emote.Name}", Kind = EmojiKind.MatrixCustom, Name = name,
            Shortcode = emote.Name, CategoryId = "custom", MxcUri = emote.Source, PackId = pack,
            SearchKey = EmojiCatalog.Normalize($"{name} {emote.Name} {pack}"),
        };
    }

    private void ScheduleFilter()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = FilterAsync(_searchCancellation.Token);
    }

    private async Task FilterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(125, cancellationToken);
            var query = EmojiCatalog.Normalize(SearchText);
            var category = SelectedCategory?.Id ?? "recent";
            var result = await Task.Run(() => Filter(query, category), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await MainThread.InvokeOnMainThreadAsync(() => VisibleItems = result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private IReadOnlyList<EmojiItem> Filter(string query, string category)
    {
        var all = Mode == EmojiPickerMode.Composer ? _unicodeItems.Concat(_customItems).ToArray() : _unicodeItems;
        var byId = all.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            if (category == "recent")
                return _usage.RecentEmoji
                    .Select(emoji => all.FirstOrDefault(item => item.Unicode == emoji))
                    .OfType<EmojiItem>()
                    .ToArray();
            if (category == "favourites")
                return _usage.FavouriteIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            return all.Where(item => item.CategoryId == category).Take(ResultLimit).ToArray();
        }

        return all.Where(item => Mode == EmojiPickerMode.Composer || item.Kind == EmojiKind.Unicode)
            .Select(item => (Item: item, Score: Score(item, query)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Item.Name, StringComparer.Ordinal)
            .Take(ResultLimit).Select(result => result.Item).ToArray();
    }

    private int Score(EmojiItem item, string query)
    {
        var shortcode = EmojiCatalog.Normalize(item.Shortcode);
        var name = EmojiCatalog.Normalize(item.Name);
        var score = shortcode == query ? 600 : shortcode.StartsWith(query) ? 500 : name.StartsWith(query) ? 400 : item.SearchKey.StartsWith(query) ? 300 : item.SearchKey.Contains(query) ? 100 : 0;
        return score + (_usage.RecentEmoji.Contains(item.Unicode) ? 10 : 0);
    }

    private void OnEmojiSelected(object? sender, SelectionChangedEventArgs args)
    {
        _grid.SelectedItem = null;
        if (args.CurrentSelection.FirstOrDefault() is not EmojiItem item || (Mode == EmojiPickerMode.Reaction && (item.Kind != EmojiKind.Unicode || !UnicodeEmoji.IsValid(item.Unicode))))
            return;
        if (item.Variants.Count > 0)
        {
            _ = PickVariantAsync(item);
            return;
        }
        Select(item);
    }

    private async Task PickVariantAsync(EmojiItem item)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;
        await page.ShowPopupAsync(new EmojiVariantPopup(item, Select, EmojiFontFamily));
    }

    private void Select(EmojiItem item)
    {
        _usage.Record(item.Unicode);
        SelectedCommand?.Execute(new EmojiSelection { Kind = item.Kind, Id = item.Id, Unicode = item.Unicode, MxcUri = item.MxcUri, Shortcode = item.Shortcode });
    }

    private sealed class EmojiItemTemplateSelector(EmojiPicker picker) : DataTemplateSelector
    {
        private readonly DataTemplate _unicode = new(() => new Label
        {
            FontSize = 25, WidthRequest = 46, HeightRequest = 46, HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
        }.Bind(Label.TextProperty, nameof(EmojiItem.Unicode)).Bind(Label.FontFamilyProperty, nameof(EmojiFontFamily), source: picker).Bind(SemanticProperties.DescriptionProperty, nameof(EmojiItem.Name)));
        private readonly DataTemplate _custom = new(() => new MatrixImage
        {
            IsJson = false, UseRoomImageCache = true, ThumbnailWidth = 64, ThumbnailHeight = 64, WidthRequest = 46, HeightRequest = 46, HorizontalOptions = LayoutOptions.Center, Aspect = Aspect.AspectFit,
        }.Bind(MatrixImage.MatrixSourceProperty, nameof(EmojiItem.MxcUri)).Bind(SemanticProperties.DescriptionProperty, nameof(EmojiItem.Name), stringFormat: "{0}, custom emote"));
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => ((EmojiItem)item).Kind == EmojiKind.Unicode ? _unicode : _custom;
    }
}

internal sealed class EmojiVariantPopup : Popup
{
    public EmojiVariantPopup(EmojiItem item, Action<EmojiItem> select, string? emojiFontFamily)
    {
        var choices = new[] { item }.Concat(item.Variants).ToArray();
        var grid = new CollectionView
        {
            SelectionMode = SelectionMode.Single, HeightRequest = 56,
            ItemsLayout = new GridItemsLayout(Math.Min(choices.Length, 8), ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() => new Label { FontSize = 26, FontFamily = emojiFontFamily, WidthRequest = 46, HeightRequest = 46, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
                .Bind(Label.TextProperty, nameof(EmojiItem.Unicode)).Bind(SemanticProperties.DescriptionProperty, nameof(EmojiItem.Name))),
            ItemsSource = choices,
        };
        grid.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is EmojiItem choice)
                select(choice);
            await CloseAsync();
        };
        Content = new Border { Padding = 8, StrokeThickness = 0, Content = grid }
            .DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}

public static class EmojiCatalog
{
    private static readonly Lazy<Task<IReadOnlyList<EmojiItem>>> LoadedItems = new(LoadItemsAsync);

    public static Task<IReadOnlyList<EmojiItem>> GetItemsAsync() => LoadedItems.Value;

    public static async Task<IReadOnlyList<EmojiCategory>> GetCategoriesAsync() =>
        (await GetItemsAsync())
            .GroupBy(item => item.CategoryId)
            .Select(group => new EmojiCategory(group.Key, group.Key, group.First().Unicode!))
            .ToArray();

    private static async Task<IReadOnlyList<EmojiItem>> LoadItemsAsync()
    {
        await using var stream = await FileSystem
            .OpenAppPackageFileAsync("emoji-test.txt")
            .ConfigureAwait(false);
        return await Task.Run(() => ParseItems(stream)).ConfigureAwait(false);
    }

    private static IReadOnlyList<EmojiItem> ParseItems(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var group = "Symbols";
        var parsed = new List<EmojiItem>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# group: ", StringComparison.Ordinal))
            {
                group = line[9..];
                continue;
            }
            if (!line.Contains("; fully-qualified", StringComparison.Ordinal))
                continue;

            var hash = line.IndexOf('#');
            var semicolon = line.IndexOf(';');
            if (hash < 0 || semicolon < 0)
                continue;
            var sequence = string.Concat(line[..semicolon].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(hex => char.ConvertFromUtf32(Convert.ToInt32(hex, 16))));
            var comment = line[(hash + 1)..].Trim(); // emoji, version, English CLDR name
            var firstSpace = comment.IndexOf(' ');
            var secondSpace = firstSpace < 0 ? -1 : comment.IndexOf(' ', firstSpace + 1);
            var name = secondSpace < 0 ? sequence : comment[(secondSpace + 1)..];
            var shortcode = string.Join('_', name.Split([' ', '-', ':', ','], StringSplitOptions.RemoveEmptyEntries));
            parsed.Add(new EmojiItem
            {
                Id = "unicode:" + string.Join('-', sequence.EnumerateRunes().Select(rune => rune.Value.ToString("x"))),
                Kind = EmojiKind.Unicode, Name = name, Shortcode = shortcode, CategoryId = group, Unicode = sequence,
                SearchKey = Normalize($"{name} {shortcode}"),
            });
        }

        return parsed
            .GroupBy(item => WithoutSkinTone(item.Unicode!))
            .Select(group =>
            {
                var baseItem = group.FirstOrDefault(item => item.Unicode == group.Key);
                return baseItem is null
                    ? null
                    : baseItem with { Variants = group.Where(item => item != baseItem).ToArray() };
            })
            .OfType<EmojiItem>()
            .ToArray();
    }

    private static string WithoutSkinTone(string value) => string.Concat(value.EnumerateRunes().Where(rune => rune.Value is < 0x1F3FB or > 0x1F3FF).Select(rune => rune.ToString()));

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

}
