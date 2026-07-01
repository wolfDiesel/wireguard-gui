using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WireguardGui.App.Avalonia.Controls;

public class AppButton : Button
{
    public static readonly StyledProperty<AppButtonVariant> VariantProperty =
        AvaloniaProperty.Register<AppButton, AppButtonVariant>(nameof(Variant), AppButtonVariant.Primary);

    private Border? _wave0;
    private Border? _wave1;
    private Border? _wave2;

    static AppButton()
    {
        VariantProperty.Changed.AddClassHandler<AppButton>((button, _) => button.SyncVariantClass());
    }

    public AppButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public AppButton()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncVariantClass();

        _wave0 = e.NameScope.Find<Border>("PART_Wave0");
        _wave1 = e.NameScope.Find<Border>("PART_Wave1");
        _wave2 = e.NameScope.Find<Border>("PART_Wave2");
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        PlayRipple();
    }

    private void PlayRipple()
    {
        if (!IsEffectivelyEnabled || _wave0 is null || _wave1 is null || _wave2 is null)
            return;

        AppButtonRipple.Play([_wave0, _wave1, _wave2], ResolveRippleBrush());
    }

    private IBrush ResolveRippleBrush()
    {
        if (Variant == AppButtonVariant.Primary)
            return new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));

        if (global::Avalonia.Application.Current?.Resources.TryGetValue("AccentPrimaryBrush", out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse("#F07818"));
    }

    private void SyncVariantClass()
    {
        Classes.Set("primary", Variant == AppButtonVariant.Primary);
        Classes.Set("ghost", Variant == AppButtonVariant.Ghost);
        Classes.Set("nav", Variant == AppButtonVariant.Nav);
        Classes.Set("option-chip", Variant == AppButtonVariant.OptionChip);
        Classes.Set("palette-chip", Variant == AppButtonVariant.PaletteChip);
        Classes.Set("toast-close", Variant == AppButtonVariant.ToastClose);
    }
}
