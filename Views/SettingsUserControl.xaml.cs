using System.Windows.Controls;

namespace Flow.Launcher.Plugin.RecentlyUsed.Views
{
    /// <summary>
    /// Interaction logic for SettingsUserControl.xaml
    /// </summary>
    public partial class SettingsUserControl : UserControl
    {
        public SettingsUserControl(Settings settings)
        {
            InitializeComponent();
            DataContext = settings;
        }
    }
}
