using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.App
{
    public static class Extensions
    {

        public static TBindable BindService<TService, TBindable>(
            this TBindable bindable,
            BindableProperty targetProperty,
            BindingMode mode = BindingMode.Default)
            where TBindable : BindableObject
            where TService : notnull
        {
            ArgumentNullException.ThrowIfNull(bindable);
            ArgumentNullException.ThrowIfNull(targetProperty);

            var service = App.Services.GetRequiredService<TService>();

            bindable.SetBinding(
                targetProperty,
                new Binding(
                    path: ".",
                    mode: mode,
                    source: service));

            return bindable;
        }

    }
}
