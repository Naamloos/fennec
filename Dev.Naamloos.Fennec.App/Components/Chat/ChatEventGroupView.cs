using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using MPowerKit.VirtualizeListView;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class ChatEventGroup : ObservableModel
{
    private bool _isExpanded;

    public ObservableCollection<ChatTimelineItem> Items { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
                Raise(nameof(ToggleText));
        }
    }

    public string ToggleText => $"{(IsExpanded ? "Hide" : "Show")} {Items.Count} room events";

    public void Replace(IEnumerable<ChatTimelineItem> items)
    {
        var desired = items.ToArray();
        var oldCount = Items.Count;
        var prefix = 0;
        while (
            prefix < Items.Count
            && prefix < desired.Length
            && ReferenceEquals(Items[prefix], desired[prefix])
        )
            prefix++;

        var suffix = 0;
        while (
            suffix < Items.Count - prefix
            && suffix < desired.Length - prefix
            && ReferenceEquals(Items[^(suffix + 1)], desired[^(suffix + 1)])
        )
            suffix++;

        var oldMiddleCount = Items.Count - prefix - suffix;
        while (oldMiddleCount-- > 0)
            Items.RemoveAt(prefix);
        for (var index = prefix; index < desired.Length - suffix; index++)
            Items.Insert(index, desired[index]);

        if (Items.Count != oldCount)
            Raise(nameof(ToggleText));
    }
}

public sealed partial class ChatEventGroupView : VirtualizeListViewCell
{
    [BindableProperty]
    public partial ChatEventGroup? Group { get; set; }

    [BindableProperty]
    public partial ICommand? MenuCommand { get; set; }

    public ChatEventGroupView()
    {
        GestureRecognizers.Clear();
        Content = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Border
                {
                    Margin = new Thickness(12, 3),
                    HorizontalOptions = LayoutOptions.Center,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = 10,
                    },
                    Content = new Grid
                    {
                        Children =
                        {
                            new BoxView { CornerRadius = 10, Opacity = .25 }.DynamicResource(
                                BoxView.ColorProperty,
                                "SurfaceContainer"
                            ),
                            new Button
                            {
                                Padding = new Thickness(10, 6),
                                MinimumHeightRequest = 44,
                                FontSize = 12,
                                BackgroundColor = Colors.Transparent,
                            }
                                .Bind(
                                    Button.TextProperty,
                                    $"{nameof(Group)}.{nameof(ChatEventGroup.ToggleText)}",
                                    source: this
                                )
                                .BindCommand(nameof(ToggleCommand), source: this),
                        },
                    },
                },
                new VerticalStackLayout { Spacing = 1 }
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(Group)}.{nameof(ChatEventGroup.IsExpanded)}",
                        source: this
                    )
                    .Bind(
                        Microsoft.Maui.Controls.BindableLayout.ItemsSourceProperty,
                        $"{nameof(Group)}.{nameof(ChatEventGroup.Items)}",
                        source: this
                    )
                    .Invoke(layout =>
                        Microsoft.Maui.Controls.BindableLayout.SetItemTemplate(
                            layout,
                            new DataTemplate(() =>
                                new ChatEventView()
                                    .Bind(ChatEventView.ItemProperty, ".")
                                    .Bind(
                                        ChatEventView.MenuCommandProperty,
                                        nameof(MenuCommand),
                                        source: this
                                    )
                            )
                        )
                    ),
            },
        };
    }

    protected override void OnAppearing()
    {
        Group = BindingContext as ChatEventGroup;
        base.OnAppearing();
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Group is null)
            return;

        Group.IsExpanded = !Group.IsExpanded;
        InvalidateMeasure();
    }
}
