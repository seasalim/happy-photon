using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class StatusBarView : UserControl
{
    public StatusBarView()
    {
        InitializeComponent();
        LayoutUpdated += async (_, _) =>
        {
            if (StatusText.IsEffectivelyVisible && StatusText.Bounds.Width > 0 &&
                DataContext is MainWindowViewModel vm)
                await vm.AcknowledgeBackupNoticeAsync(StatusText.Text);
        };
    }
}
