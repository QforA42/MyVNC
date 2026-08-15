using System.Windows.Markup;

namespace MyVNC.App.Services;

/// <summary>
/// XAML markup extension for static text: <c>Text="{loc:Loc Card.Edit}"</c>. Resolves once, at
/// the moment the element is created, using whatever language is current at that time — which
/// is enough here because MainWindow rebuilds itself (see MainWindow.xaml.cs) whenever the
/// language changes, so every element (including per-item DataTemplate content) gets freshly
/// created and re-resolved rather than needing a live-binding update mechanism.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
