using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimelineItemView : ContentView
{
    [BindableProperty]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial ICommand? ReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? EditCommand { get; set; }

    [BindableProperty]
    public partial ICommand? LinkCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MenuCommand { get; set; }

    [BindableProperty]
    public partial ICommand? AddReactionCommand { get; set; }

    [BindableProperty]
    public partial ICommand? OpenMediaCommand { get; set; }

    public ChatTimelineItemView()
    {
        Content = new TemplateSwitchView<ChatTimelineItem, bool>(
            item => item.IsMessage)
        {
            FallbackTemplate = new ChatEventView()
                .Bind(ChatEventView.ItemProperty, ".")
                .Bind(ChatEventView.MenuCommandProperty,
                    nameof(MenuCommand), source: this),
        }
        .Add(
            isMessage => isMessage,
            new ChatMessageView()
                .Bind(ChatMessageView.ItemProperty, ".")
                .Bind(ChatMessageView.ClientProperty, nameof(Client), source: this)
                .Bind(ChatMessageView.MembersProperty, nameof(Members), source: this)
                .Bind(ChatMessageView.ReplyCommandProperty, nameof(ReplyCommand), source: this)
                .Bind(ChatMessageView.EditCommandProperty, nameof(EditCommand), source: this)
                .Bind(ChatMessageView.LinkCommandProperty, nameof(LinkCommand), source: this)
                .Bind(ChatMessageView.MenuCommandProperty, nameof(MenuCommand), source: this)
                .Bind(ChatMessageView.AddReactionCommandProperty,
                    nameof(AddReactionCommand), source: this)
                .Bind(ChatMessageView.OpenMediaCommandProperty,
                    nameof(OpenMediaCommand), source: this))
        .Bind(TemplateSwitchView<ChatTimelineItem, bool>.ValueProperty,
            nameof(Item), source: this);
    }
}
