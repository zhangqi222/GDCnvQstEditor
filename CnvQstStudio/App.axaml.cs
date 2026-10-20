using Avalonia;
using Avalonia.Markup.Xaml;

namespace CnvQstStudio;

/// <summary>Avalonia 应用定义（Semi.Avalonia 主题在 App.axaml 挂载）。</summary>
public partial class StudioApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}