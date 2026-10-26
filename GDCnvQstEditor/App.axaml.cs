using Avalonia;
using Avalonia.Markup.Xaml;

namespace GDCnvQstEditor;

/// <summary>Avalonia 应用定义（Semi.Avalonia 主题在 App.axaml 挂载）。</summary>
public partial class EditorApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}