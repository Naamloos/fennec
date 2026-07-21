using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.App
{
    public static class Extensions
    {
        /// <summary>
        /// Fluent extension method to bind a property of a BindableObject to a specified path with an optional binding mode.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="control"></param>
        /// <param name="property"></param>
        /// <param name="path"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static T Bind<T>(
            this T control,
            BindableProperty property,
            string path,
            BindingMode mode = BindingMode.Default,
            IValueConverter? converter = null)
            where T : BindableObject
        {
            control.SetBinding(
                property,
                new Binding(path, mode, converter));

            return control;
        }
    }
}
