using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using WireguardGui.App.Avalonia.ViewModels;

namespace WireguardGui.App.Avalonia;

[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is not null)
        {
            var control = (Control)Activator.CreateInstance(type)!;
            control.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            control.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            return control;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
