using System.Windows.Input;
using CommunityToolkit.Maui;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class AttachmentEntry : Entry
{
    [BindableProperty]
    public partial ICommand? AttachmentCommand { get; set; }

    internal void ReceiveAttachments(IReadOnlyList<PickedAttachment> attachments)
    {
        if (attachments.Count > 0 && AttachmentCommand?.CanExecute(attachments) == true)
        {
            AttachmentCommand.Execute(attachments);
        }
    }
}
