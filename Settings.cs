using System.ComponentModel;

namespace Flow.Launcher.Plugin.RecentlyUsed
{
    public class Settings : INotifyPropertyChanged
    {
        private bool _showFolders = true;
        public bool ShowFolders
        {
            get => _showFolders;
            set
            {
                if (_showFolders != value)
                {
                    _showFolders = value;
                    OnPropertyChanged(nameof(ShowFolders));
                }
            }
        }

        private bool _showAccessedDate;
        public bool ShowAccessedDate
        {
            get => _showAccessedDate;
            set
            {
                if (_showAccessedDate != value)
                {
                    _showAccessedDate = value;
                    OnPropertyChanged(nameof(ShowAccessedDate));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
