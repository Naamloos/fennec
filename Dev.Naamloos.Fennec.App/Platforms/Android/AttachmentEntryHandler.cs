using Android.Content;
using Android.Provider;
using Android.Views.InputMethods;
using Android.Webkit;
using AndroidX.Core.View;
using AndroidX.Core.View.InputMethod;
using Dev.Naamloos.Fennec.App.Components;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Dev.Naamloos.Fennec.App.Platforms.Android;

public sealed class AttachmentEntryHandler : EntryHandler
{
    private static readonly string[] AcceptedMimeTypes = ["image/*"];
    private ReceiveContentListener? _listener;

    protected override MauiAppCompatEditText CreatePlatformView() =>
        new AttachmentEditText(Context);

    protected override void ConnectHandler(MauiAppCompatEditText platformView)
    {
        base.ConnectHandler(platformView);
        _listener = new ReceiveContentListener(this);
        ViewCompat.SetOnReceiveContentListener(
            platformView,
            AcceptedMimeTypes,
            _listener);
    }

    protected override void DisconnectHandler(MauiAppCompatEditText platformView)
    {
        ViewCompat.SetOnReceiveContentListener(platformView, null, null);
        _listener?.Dispose();
        _listener = null;
        base.DisconnectHandler(platformView);
    }

    private async Task ReceiveAsync(
        IReadOnlyList<global::Android.Net.Uri> uris,
        ContentInfoCompat permissionLease)
    {
        var resolver = PlatformView?.Context?.ContentResolver;
        if (resolver is null)
        {
            return;
        }

        try
        {
            var attachments = new List<PickedAttachment>(uris.Count);
            foreach (var uri in uris)
            {
                await using var input = resolver.OpenInputStream(uri);
                if (input is null)
                {
                    continue;
                }

                using var data = new MemoryStream();
                await input.CopyToAsync(data);
                var mimeType = resolver.GetType(uri) ?? "image/png";
                attachments.Add(new PickedAttachment(
                    FileName(resolver, uri, mimeType),
                    mimeType,
                    data.ToArray()));
            }

            if (attachments.Count > 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    (VirtualView as AttachmentEntry)?.ReceiveAttachments(attachments));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not receive keyboard content: {exception}");
        }
        finally
        {
            GC.KeepAlive(permissionLease);
        }
    }

    private static string FileName(
        ContentResolver resolver,
        global::Android.Net.Uri uri,
        string mimeType)
    {
        using var cursor = resolver.Query(
            uri,
            [IOpenableColumns.DisplayName],
            null,
            null,
            null);
        if (cursor?.MoveToFirst() == true)
        {
            var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
            if (index >= 0 && cursor.GetString(index) is { Length: > 0 } name)
            {
                return name;
            }
        }

        var extension = MimeTypeMap.Singleton?.GetExtensionFromMimeType(mimeType);
        return $"keyboard-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}." +
               (string.IsNullOrWhiteSpace(extension) ? "png" : extension);
    }

    private sealed class ReceiveContentListener(AttachmentEntryHandler handler) :
        Java.Lang.Object,
        IOnReceiveContentListener
    {
        public ContentInfoCompat? OnReceiveContent(
            global::Android.Views.View? view,
            ContentInfoCompat? payload)
        {
            var clip = payload?.Clip;
            if (clip is null)
            {
                return payload;
            }

            var uris = Enumerable.Range(0, clip.ItemCount)
                .Select(index => clip.GetItemAt(index)?.Uri)
                .Where(uri => uri is not null)
                .Cast<global::Android.Net.Uri>()
                .ToArray();
            if (uris.Length == 0)
            {
                return payload;
            }

            _ = handler.ReceiveAsync(uris, payload!);
            return null;
        }
    }

    private sealed class AttachmentEditText(Context context) : MauiAppCompatEditText(context)
    {
        public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
        {
            var inputConnection = base.OnCreateInputConnection(outAttrs);
            if (inputConnection is null || outAttrs is null)
            {
                return inputConnection;
            }

            EditorInfoCompat.SetContentMimeTypes(outAttrs, AcceptedMimeTypes);
            return InputConnectionCompat.CreateWrapper(
                this,
                inputConnection,
                outAttrs);
        }
    }
}
