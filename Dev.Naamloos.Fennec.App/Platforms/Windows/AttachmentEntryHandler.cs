using Dev.Naamloos.Fennec.App.Components;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using NativeDataPackageView = Windows.ApplicationModel.DataTransfer.DataPackageView;
using NativeDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using NativeDragEventArgs = Microsoft.UI.Xaml.DragEventArgs;
using NativeClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Dev.Naamloos.Fennec.App.Platforms.Windows;

public sealed class AttachmentEntryHandler : EntryHandler
{
    protected override void ConnectHandler(TextBox platformView)
    {
        base.ConnectHandler(platformView);
        platformView.AllowDrop = true;
        platformView.KeyDown += OnKeyDown;
        platformView.DragOver += OnDragOver;
        platformView.Drop += OnDrop;
    }

    protected override void DisconnectHandler(TextBox platformView)
    {
        platformView.KeyDown -= OnKeyDown;
        platformView.DragOver -= OnDragOver;
        platformView.Drop -= OnDrop;
        base.DisconnectHandler(platformView);
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.V || !ControlIsDown())
        {
            return;
        }

        var content = NativeClipboard.GetContent();
        if (!HasAttachments(content))
        {
            return;
        }

        args.Handled = true;
        await ReceiveAsync(content);
    }

    private static void OnDragOver(object sender, NativeDragEventArgs args)
    {
        if (HasAttachments(args.DataView))
        {
            args.AcceptedOperation = NativeDataPackageOperation.Copy;
            args.Handled = true;
        }
    }

    private async void OnDrop(object sender, NativeDragEventArgs args)
    {
        if (!HasAttachments(args.DataView))
        {
            return;
        }

        args.AcceptedOperation = NativeDataPackageOperation.Copy;
        args.Handled = true;
        await ReceiveAsync(args.DataView);
    }

    private async Task ReceiveAsync(NativeDataPackageView content)
    {
        try
        {
            var attachments = await ReadAttachmentsAsync(content);
            if (attachments.Count > 0)
            {
                (VirtualView as AttachmentEntry)?.ReceiveAttachments(attachments);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not receive pasted or dropped content: {exception}");
        }
    }

    private static async Task<IReadOnlyList<PickedAttachment>> ReadAttachmentsAsync(
        NativeDataPackageView content)
    {
        var attachments = new List<PickedAttachment>();

        if (content.Contains(StandardDataFormats.StorageItems))
        {
            foreach (var file in (await content.GetStorageItemsAsync()).OfType<StorageFile>())
            {
                await using var input = await file.OpenStreamForReadAsync();
                attachments.Add(await ReadAsync(
                    file.Name,
                    string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    input));
            }
        }

        if (attachments.Count == 0 && content.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await content.GetBitmapAsync();
            using var randomAccess = await reference.OpenReadAsync();
            await using var input = randomAccess.AsStreamForRead();
            var mimeType = string.IsNullOrWhiteSpace(randomAccess.ContentType)
                ? "image/png"
                : randomAccess.ContentType;
            attachments.Add(await ReadAsync(
                $"clipboard-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{Extension(mimeType)}",
                mimeType,
                input));
        }

        return attachments;
    }

    private static async Task<PickedAttachment> ReadAsync(
        string fileName,
        string mimeType,
        Stream input)
    {
        using var data = new MemoryStream();
        await input.CopyToAsync(data);
        return new PickedAttachment(fileName, mimeType, data.ToArray());
    }

    private static bool HasAttachments(NativeDataPackageView content) =>
        content.Contains(StandardDataFormats.StorageItems) ||
        content.Contains(StandardDataFormats.Bitmap);

    private static bool ControlIsDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

    private static string Extension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/gif" => ".gif",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".png",
    };
}
