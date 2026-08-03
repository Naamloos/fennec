using uniffi.matrix_sdk;

namespace Dev.Naamloos.Fennec.App.Components;

public class TemplateSwitchView<TValue, TInput> : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(object),
        typeof(TemplateSwitchView<TValue, TInput>),
        propertyChanged: static (bindable, _, newValue) =>
            ((TemplateSwitchView<TValue, TInput>)bindable).Build()
    );

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private TemplateInputSelectorDelegate _inputSelector;
    private IDictionary<TemplatePropertySelectorDelegate, View> _templateSelectors =
        new Dictionary<TemplatePropertySelectorDelegate, View>();

    public View? FallbackTemplate = null;

    public TemplateSwitchView(TemplateInputSelectorDelegate inputSelector)
    {
        _inputSelector = inputSelector;
        _templateSelectors = new Dictionary<TemplatePropertySelectorDelegate, View>();

        Build();
    }

    public TemplateSwitchView<TValue, TInput> Add(
        TemplatePropertySelectorDelegate selector,
        View template
    )
    {
        _templateSelectors[selector] = template;
        return this;
    }

    private void Build()
    {
        if (Value is not TValue value)
        {
            BindingContext = null;
            Content = FallbackTemplate;
            return;
        }

        this.BindingContext = value;

        View? outputView = null;

        TInput inputValue = _inputSelector(value);

        foreach (var selector in _templateSelectors)
        {
            if (selector.Key(inputValue))
            {
                outputView = selector.Value;
                break;
            }
        }

        Content = outputView ?? FallbackTemplate;
    }

    public delegate TInput TemplateInputSelectorDelegate(TValue value);
    public delegate bool TemplatePropertySelectorDelegate(TInput value);
}
