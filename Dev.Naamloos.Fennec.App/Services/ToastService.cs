using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.App.Services
{
    public interface IToastService
    {
        Task ShowToastAsync(string message, ToastDuration duration = ToastDuration.Short, CancellationToken cancellationToken = default);
    }

    public sealed class ToastService : IToastService
    {
        private const double DefaultFontSize = 14;

        public ToastService()
        {
        }

        public Task ShowToastAsync(
            string message,
            ToastDuration duration = ToastDuration.Short,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var toast = Toast.Make(
                    message,
                    duration,
                    DefaultFontSize);

                // TODO fix windows toast issue
                //await toast.Show(cancellationToken);
            });
        }
    }
}
