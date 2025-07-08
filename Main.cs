using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentlyUsed;
using Flow.Launcher.Plugin.RecentlyUsed.Helper;
using Flow.Launcher.Plugin.RecentlyUsed.Views;
using System.Windows.Controls;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// IPluginI18n 및 IDisposable 인터페이스를 구현합니다.
public class Main : IPlugin, ISettingProvider, IPluginI18n, IDisposable
{
    private PluginInitContext context;
    private string recentFolder;
    private Settings settings;

    private List<RecentItem> _cachedRecentItems = new List<RecentItem>();
    private string _cacheFilePath;
    private bool _isCacheUpdating = false;
    private FileSystemWatcher _watcher;
    private readonly Timer _updateTimer;

    public Main()
    {
        // 짧은 시간 내의 여러 변경 이벤트를 하나로 묶어 처리하기 위한 타이머 (500ms 지연)
        _updateTimer = new Timer(_ => UpdateCache(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Init(PluginInitContext context)
    {
        this.context = context;
        recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        settings = context.API.LoadSettingJsonStorage<Settings>();
        _cacheFilePath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache.json");

        LoadCacheFromFile();

        if (Directory.Exists(recentFolder))
        {
            _watcher = new FileSystemWatcher
            {
                Path = recentFolder,
                Filter = "*.lnk",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnRecentFolderChanged;
            _watcher.Created += OnRecentFolderChanged;
            _watcher.Deleted += OnRecentFolderChanged;
            _watcher.Renamed += OnRecentFolderChanged;
        }

        // 초기 캐시 업데이트 (백그라운드 실행)
        Task.Run(UpdateCache);
    }

    public Control CreateSettingPanel()
    {
        return new SettingsUserControl(settings);
    }

    public string GetTranslatedPluginTitle()
    {
        return T("flow_plugin_recentlyused_plugin_name");
    }

    public string GetTranslatedPluginDescription()
    {
        return T("flow_plugin_recentlyused_plugin_description");
    }

    public void OnCultureInfoChanged(CultureInfo newCulture)
    {
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }

    public List<Result> Query(Query query)
    {
        var searchTerm = query.Search?.Trim() ?? "";

        // 캐시된 데이터를 기반으로 검색
        var filteredItems = string.IsNullOrEmpty(searchTerm)
            ? _cachedRecentItems
            : _cachedRecentItems.AsParallel()
                .Where(item =>
                    (item.FileName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.TargetFileName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Title?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.SubTitle?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.TargetPath?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                ).ToList();

        var results = new List<Result>();
        foreach (var item in filteredItems)
        {
            string displaySubTitle = item.SubTitle;
            if (settings.ShowAccessedDate)
            {
                string formattedDate = FormatDateTime(item.DateModified);
                displaySubTitle = $"{formattedDate} | {item.SubTitle}";
            }

            results.Add(new Result
            {
                Title = item.Title,
                SubTitle = displaySubTitle,
                IcoPath = item.LnkPath,
                AutoCompleteText = item.TargetPath,
                AddSelectedCount = false,
                Action = _ =>
                {
                    try
                    {
                        context.API.ShellRun($"\"{item.LnkPath}\"");
                    }
                    catch { }
                    return true;
                }
            });
        }
        return results;
    }

    private void OnRecentFolderChanged(object sender, FileSystemEventArgs e)
    {
        // 변경 이벤트 발생 시, 500ms 후에 캐시 업데이트를 예약합니다.
        _updateTimer.Change(500, Timeout.Infinite);
    }

    private void UpdateCache()
    {
        if (_isCacheUpdating) return;
        _isCacheUpdating = true;

        try
        {
            var newCache = new List<RecentItem>();
            if (Directory.Exists(recentFolder))
            {
                var files = GetRecentLnkFiles(recentFolder);
                foreach (var fileInfo in files)
                {
                    var lnkPath = fileInfo.FullName;
                    var fileName = Path.GetFileNameWithoutExtension(lnkPath);
                    string targetPath = ShellLinkHelper.ResolveShortcut(lnkPath);

                    if (string.IsNullOrEmpty(targetPath)) continue;
                    if (!Path.IsPathRooted(targetPath)) { try { targetPath = Path.GetFullPath(targetPath); } catch { } }

                    bool isFolder = Directory.Exists(targetPath);
                    bool isFile = File.Exists(targetPath);
                    bool isUrl = Uri.IsWellFormedUriString(targetPath, UriKind.Absolute) || targetPath.StartsWith("onenote:", StringComparison.OrdinalIgnoreCase);

                    if (!settings.ShowFolders && isFolder) continue;
                    if (!isFile && !isFolder && !isUrl) continue;

                    string targetFileName = Path.GetFileName(targetPath);
                    string title = Path.GetFileName(targetPath);
                    string subTitle = Path.GetDirectoryName(targetPath);
                    bool isDriveRoot = false;

                    if (string.IsNullOrEmpty(title) && Directory.Exists(targetPath))
                    {
                        try
                        {
                            isDriveRoot = Path.GetPathRoot(targetPath) == targetPath;
                            if (isDriveRoot)
                            {
                                title = fileName;
                                if (title.EndsWith(")"))
                                {
                                    int openParenIndex = title.LastIndexOf('(');
                                    if (openParenIndex > 0 && openParenIndex + 2 < title.Length)
                                    {
                                        char driveLetter = title[openParenIndex + 1];
                                        if (char.IsLetter(driveLetter) && title[openParenIndex + 2] == ')')
                                        {
                                            title = title.Insert(openParenIndex + 2, ":");
                                        }
                                    }
                                }
                                subTitle = targetPath;
                            }
                        }
                        catch { title = targetPath; subTitle = targetPath; }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(title)) title = targetPath;
                        if (string.IsNullOrEmpty(subTitle)) subTitle = targetPath;
                    }

                    newCache.Add(new RecentItem
                    {
                        LnkPath = lnkPath,
                        FileName = fileName,
                        TargetPath = targetPath,
                        TargetFileName = targetFileName,
                        Title = title,
                        SubTitle = subTitle,
                        IsFolder = isFolder,
                        IsDriveRoot = isDriveRoot,
                        DateModified = fileInfo.LastWriteTime
                    });
                }
            }
            _cachedRecentItems = newCache;
            SaveCacheToFile();
        }
        catch (Exception ex)
        {
            context.API.LogException(nameof(Main), "Failed to update cache.", ex);
        }
        finally
        {
            _isCacheUpdating = false;
        }
    }

    private void SaveCacheToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cachedRecentItems);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            context.API.LogException(nameof(Main), "Failed to save cache.", ex);
        }
    }

    private void LoadCacheFromFile()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                var items = JsonSerializer.Deserialize<List<RecentItem>>(json);
                if (items != null)
                {
                    _cachedRecentItems = items;
                }
            }
        }
        catch (Exception ex)
        {
            context.API.LogException(nameof(Main), "Failed to load cache.", ex);
            _cachedRecentItems = new List<RecentItem>();
        }
    }

    private string FormatDateTime(DateTime dateTime)
    {
        var now = DateTime.Now;
        var diff = now - dateTime;
        var culture = CultureInfo.CurrentUICulture;

        if (diff.TotalHours < 1) return T("flow_plugin_recentlyused_just_now");
        if (diff.TotalHours < 2) return $"1 {T("flow_plugin_recentlyused_hour_ago")}";
        if (now.Date == dateTime.Date) return $"{(int)diff.TotalHours} {T("flow_plugin_recentlyused_hours_ago")}";
        if (now.Date.AddDays(-1) == dateTime.Date) return $"{T("flow_plugin_recentlyused_yesterday")} {dateTime:HH:mm}";
        if (diff.TotalDays < 7) return $"{culture.DateTimeFormat.GetDayName(dateTime.DayOfWeek)} {dateTime:HH:mm}";
        
        return dateTime.ToString("M", culture);
    }

    private string T(string key)
    {
        return context.API.GetTranslation(key);
    }

    private List<FileInfo> GetRecentLnkFiles(string recentFolder)
    {
        try
        {
            return Directory.GetFiles(recentFolder, "*.lnk")
                               .Select(f => new FileInfo(f))
                               .OrderByDescending(f => f.LastWriteTime)
                               .ToList();
        }
        catch (Exception ex)
        {
            context.API.LogException(nameof(Main), "Failed to get recent lnk files.", ex);
            return new List<FileInfo>();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _updateTimer?.Dispose();
    }

    private static string GetVolumeLabel(string driveRoot)
    {
        var label = new System.Text.StringBuilder(261);
        var fs = new System.Text.StringBuilder(261);
        uint serial = 0, maxLen = 0, flags = 0;
        bool ok = GetVolumeInformation(driveRoot, label, (uint)label.Capacity, out serial, out maxLen, out flags, fs, (uint)fs.Capacity);
        return ok ? label.ToString() : string.Empty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        System.Text.StringBuilder volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        System.Text.StringBuilder fileSystemNameBuffer,
        uint nFileSystemNameSize);
}

[Serializable]
public class RecentItem
{
    public string LnkPath { get; set; }
    public string FileName { get; set; }
    public string TargetPath { get; set; }
    public string TargetFileName { get; set; }
    public string Title { get; set; }
    public string SubTitle { get; set; }
    public bool IsFolder { get; set; }
    public bool IsDriveRoot { get; set; }
    public DateTime DateModified { get; set; }
}