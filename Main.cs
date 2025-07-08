using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentlyUsed;
using Flow.Launcher.Plugin.RecentlyUsed.Helper;
using Flow.Launcher.Plugin.RecentlyUsed.Views;
using System.Windows.Controls;
using System.IO;
using System.Runtime.InteropServices;
using System.Data.OleDb;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

// IPluginI18n 인터페이스를 구현합니다.
public class Main : IPlugin, ISettingProvider, IPluginI18n
{
    private PluginInitContext context;
    private string recentFolder;
    private Settings settings;

    public void Init(PluginInitContext context)
    {
        this.context = context;
        recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        settings = context.API.LoadSettingJsonStorage<Settings>();
    }

    public Control CreateSettingPanel()
    {
        return new SettingsUserControl(settings);
    }

    // IPluginI18n 인터페이스의 멤버를 구현합니다.
    public string GetTranslatedPluginTitle()
    {
        return T("flow_plugin_recentlyused_plugin_name");
    }

    public string GetTranslatedPluginDescription()
    {
        return T("flow_plugin_recentlyused_plugin_description");
    }

    // Flow Launcher의 언어가 변경될 때 호출되는 메서드를 추가합니다.
    public void OnCultureInfoChanged(CultureInfo newCulture)
    {
        // Flow Launcher가 알려준 문화권 정보로 현재 스레드의 문화권을 명시적으로 설정합니다.
        // 이것이 Flow Launcher의 언어 설정을 올바르게 따르는 방법입니다.
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }

    public List<Result> Query(Query query)
    {
        var results = new List<Result>();

        if (!Directory.Exists(recentFolder))
            return results;

        var searchTerm = query.Search?.Trim() ?? "";
        var files = GetRecentLnkFiles(recentFolder);

        foreach (var fileInfo in files)
        {
            var lnkPath = fileInfo.FullName;
            var fileName = Path.GetFileNameWithoutExtension(lnkPath);
            string targetPath = ShellLinkHelper.ResolveShortcut(lnkPath);

            if (string.IsNullOrEmpty(targetPath))
                continue;

            // 대상 경로가 절대경로가 아닌 경우 절대 경로로 변환
            if (!Path.IsPathRooted(targetPath))
            {
                try
                {
                    targetPath = Path.GetFullPath(targetPath);
                }
                catch { }
            }

            bool isFolder = Directory.Exists(targetPath);
            bool isFile = File.Exists(targetPath);
            bool isUrl = Uri.IsWellFormedUriString(targetPath, UriKind.Absolute) ||
                         targetPath.StartsWith("onenote:", StringComparison.OrdinalIgnoreCase) ||
                         targetPath.StartsWith("onenotehttps:", StringComparison.OrdinalIgnoreCase);

            if (!settings.ShowFolders && isFolder)
                continue;

            if (!isFile && !isFolder && !isUrl)
                continue;

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
                catch
                {
                    title = targetPath;
                    subTitle = targetPath;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(title))
                    title = targetPath;
                if (string.IsNullOrEmpty(subTitle))
                    subTitle = targetPath;
            }

            if (!string.IsNullOrEmpty(searchTerm) &&
                !fileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !targetFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !subTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !targetPath.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                continue;

            string newQuery = targetPath;
            if (Directory.Exists(targetPath) && !newQuery.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                newQuery += Path.DirectorySeparatorChar;
            }

            // 설정에 따라 부제(SubTitle)를 조합합니다.
            string displaySubTitle = subTitle;
            if (settings.ShowAccessedDate)
            {
                string formattedDate = FormatDateTime(fileInfo.LastWriteTime);
                displaySubTitle = $"{formattedDate} | {subTitle}";
            }

            results.Add(new Result
            {
                Title = title,
                SubTitle = displaySubTitle,
                IcoPath = lnkPath,
                AutoCompleteText = newQuery,
                Action = _ =>
                {
                    try
                    {
                        // 경로에 공백이 포함된 경우를 처리하기 위해 따옴표로 감싸줍니다.
                        context.API.ShellRun($"\"{lnkPath}\"");
                    }
                    catch { }
                    return true; // 실행 후 Flow Launcher 창을 닫습니다.
                }
            });
        }

        return results;
    }

    private string FormatDateTime(DateTime dateTime)
    {
        var now = DateTime.Now;
        var diff = now - dateTime;
        // Flow Launcher의 언어 설정을 따르기 위해 현재 UI 문화권을 사용합니다.
        var culture = CultureInfo.CurrentUICulture;

        if (diff.TotalHours < 1)
            return T("flow_plugin_recentlyused_just_now");
        if (diff.TotalHours < 2)
            return $"1 {T("flow_plugin_recentlyused_hour_ago")}";
        if (now.Date == dateTime.Date)
            return $"{(int)diff.TotalHours} {T("flow_plugin_recentlyused_hours_ago")}";
        if (now.Date.AddDays(-1) == dateTime.Date)
            return $"{T("flow_plugin_recentlyused_yesterday")} {dateTime:HH:mm}";
        
        if (diff.TotalDays < 7)
            return $"{culture.DateTimeFormat.GetDayName(dateTime.DayOfWeek)} {dateTime:HH:mm}";
        
        return dateTime.ToString("M", culture); // 예: "4월 2일" 또는 "April 2"
    }

    private string T(string key)
    {
        return context.API.GetTranslation(key);
    }

    private List<FileInfo> GetRecentLnkFiles(string recentFolder)
    {
        var fileList = new List<FileInfo>();
        try
        {
            using (var connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
            {
                connection.Open();
                string escapedFolder = recentFolder.Replace("\\", "\\\\");
                string queryStr = $"SELECT System.ItemPathDisplay, System.DateModified FROM SYSTEMINDEX " +
                                  $"WHERE scope = 'file:{escapedFolder}' AND System.FileName LIKE '%.lnk' " +
                                  $"ORDER BY System.DateModified DESC";

                using (var command = new OleDbCommand(queryStr, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string filePath = reader["System.ItemPathDisplay"] as string;
                            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                            {
                                fileList.Add(new FileInfo(filePath));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 인덱서 조회 실패 시 기존 방식으로 대체
            fileList = Directory.GetFiles(recentFolder, "*.lnk")
                               .Select(f => new FileInfo(f))
                               .OrderByDescending(f => f.LastWriteTime)
                               .ToList();
        }
        return fileList;
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