using Microsoft.Maui.Controls.Shapes;
using CommunityToolkit.Maui.Converters;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class AccountButton : Border
{
    public static readonly BindableProperty AvatarSourceProperty = BindableProperty.Create(
        nameof(AvatarSource), typeof(ImageSource), typeof(AccountButton));
    public static readonly BindableProperty DisplayNameProperty = BindableProperty.Create(
        nameof(DisplayName), typeof(string), typeof(AccountButton), "Account");
    public static readonly BindableProperty UserIdProperty = BindableProperty.Create(
        nameof(UserId), typeof(string), typeof(AccountButton), string.Empty);
    public static readonly BindableProperty InitialProperty = BindableProperty.Create(
        nameof(Initial), typeof(string), typeof(AccountButton), "@");
    public static readonly BindableProperty ShowUserIdProperty = BindableProperty.Create(
        nameof(ShowUserId), typeof(bool), typeof(AccountButton));
    public static readonly BindableProperty TransparentBackgroundProperty = BindableProperty.Create(
        nameof(TransparentBackground), typeof(bool), typeof(AccountButton), false,
        propertyChanged: (bindable, _, _) => ((AccountButton)bindable).UpdateBackground());

    public ImageSource? AvatarSource { get => (ImageSource?)GetValue(AvatarSourceProperty); set => SetValue(AvatarSourceProperty, value); }
    public string DisplayName { get => (string)GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }
    public string UserId { get => (string)GetValue(UserIdProperty); set => SetValue(UserIdProperty, value); }
    public string Initial { get => (string)GetValue(InitialProperty); set => SetValue(InitialProperty, value); }
    public bool ShowUserId { get => (bool)GetValue(ShowUserIdProperty); set => SetValue(ShowUserIdProperty, value); }
    public bool TransparentBackground { get => (bool)GetValue(TransparentBackgroundProperty); set => SetValue(TransparentBackgroundProperty, value); }

    public event EventHandler? Clicked;
    private bool _isPointerOver;
    private bool _isPressed;

    public AccountButton()
    {
        var image = new Image { Aspect = Aspect.AspectFill, WidthRequest = 30, HeightRequest = 30 };
        image.SetBinding(Image.SourceProperty, new Binding(nameof(AvatarSource), source: this));
        var fallback = new Label { FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        fallback.SetBinding(Label.TextProperty, new Binding(nameof(Initial), source: this));
        var avatar = new Border
        {
            WidthRequest = 30, HeightRequest = 30, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Content = new Grid { Children = { fallback, image } },
        };
        avatar.SetDynamicResource(BackgroundColorProperty, "PrimaryContainer");

        var name = new Label { MaximumWidthRequest = 180, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center };
        name.SetBinding(Label.TextProperty, new Binding(nameof(DisplayName), source: this));
        var id = new Label { FontSize = 12, Opacity = .7, LineBreakMode = LineBreakMode.TailTruncation };
        id.SetBinding(Label.TextProperty, new Binding(nameof(UserId), source: this));
        var details = new VerticalStackLayout { Spacing = 0, Children = { name, id } };
        details.SetBinding(IsVisibleProperty, new Binding(nameof(ShowUserId), source: this));

        var compactName = new Label { MaximumWidthRequest = 180, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center };
        compactName.SetBinding(Label.TextProperty, new Binding(nameof(DisplayName), source: this));
        compactName.SetBinding(IsVisibleProperty, new Binding(nameof(ShowUserId), source: this, converter: new InvertedBoolConverter()));

        Padding = new Thickness(8, 6);
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 12 };
        Content = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { avatar, compactName, details } };
        UpdateBackground();
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Clicked?.Invoke(this, EventArgs.Empty)) });
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { _isPointerOver = true; UpdateBackground(); };
        pointer.PointerExited += (_, _) => { _isPointerOver = _isPressed = false; UpdateBackground(); };
        pointer.PointerPressed += (_, _) => { _isPressed = true; UpdateBackground(); };
        pointer.PointerReleased += (_, _) => { _isPressed = false; UpdateBackground(); };
        GestureRecognizers.Add(pointer);
        SemanticProperties.SetDescription(this, "Open user settings");
    }

    private void UpdateBackground()
    {
        if (_isPressed)
        {
            SetDynamicResource(BackgroundColorProperty, "SurfaceContainer");
        }
        else if (_isPointerOver)
        {
            SetDynamicResource(BackgroundColorProperty, "SurfaceVariant");
        }
        else if (TransparentBackground)
        {
            BackgroundColor = Colors.Transparent;
        }
        else
        {
            SetDynamicResource(BackgroundColorProperty, "Surface2");
        }
    }
}
