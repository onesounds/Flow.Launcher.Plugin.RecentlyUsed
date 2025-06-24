using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentlyUsed;
using Flow.Launcher.Plugin.RecentlyUsed.Helper;
using Flow.Launcher.Plugin.RecentlyUsed.Views;
using System.Windows.Controls;
using System.IO;
using System.Runtime.InteropServices;

public class Main : IPlugin, ISettingProvider
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

    public List<Result> Query(Query query)
    {
        var results = new List<Result>();

        if (!Directory.Exists(recentFolder))
            return results;

        var searchTerm = query.Search?.Trim() ?? "";

        var files = Directory.GetFiles(recentFolder, "*.lnk")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        foreach (var fileInfo in files)
        {
            var lnkPath = fileInfo.FullName;
            var fileName = Path.GetFileNameWithoutExtension(lnkPath);

            string targetPath = ShellLinkHelper.ResolveShortcut(lnkPath);

            if (string.IsNullOrEmpty(targetPath))
                continue;

            // 로깅(디버깅용, 나중에 제거 가능)
            // context.API.LogInfo($"File: {fileName}, Target: {targetPath}");

            bool isFolder = Directory.Exists(targetPath);
            bool isFile = File.Exists(targetPath);
            bool isUrl = Uri.IsWellFormedUriString(targetPath, UriKind.Absolute) ||
             targetPath.StartsWith("onenote:", StringComparison.OrdinalIgnoreCase) ||
             targetPath.StartsWith("onenotehttps:", StringComparison.OrdinalIgnoreCase);

            // URL은 항상 표시되도록 조건 수정
            if (!settings.ShowFolders && isFolder)
                continue;

            // 파일도 아니고 폴더도 아니고 URL도 아니면 건너뛰기
            if (!isFile && !isFolder && !isUrl)
                continue;

            // 대상 파일의 전체 이름 (확장자 포함)
            string targetFileName = Path.GetFileName(targetPath);
            
            // 드라이브 루트 처리
            string title = Path.GetFileName(targetPath);
            string subTitle = Path.GetDirectoryName(targetPath);
            bool isDriveRoot = false;

            if (string.IsNullOrEmpty(title) && Directory.Exists(targetPath))
            {
                try
                {
                    // 드라이브 루트 확인
                    isDriveRoot = Path.GetPathRoot(targetPath) == targetPath;

                    if (isDriveRoot)
                    {
                        // 드라이브 루트면 lnk 파일명(확장자 제외)을 사용
                        title = fileName;  // fileName은 이미 상단에서 Path.GetFileNameWithoutExtension(lnkPath)로 확장자 제외됨
                        
                        // 파일명 끝에 드라이브 문자만 있는 경우(예: "로컬 디스크 (C)")라면 콜론(:) 추가
                        if (title.EndsWith(")"))
                        {
                            int openParenIndex = title.LastIndexOf('(');
                            if (openParenIndex > 0 && openParenIndex + 2 < title.Length)
                            {
                                char driveLetter = title[openParenIndex + 1];
                                if (char.IsLetter(driveLetter) && title[openParenIndex + 2] == ')')
                                {
                                    // "로컬 디스크 (C)" -> "로컬 디스크 (C:)"로 변환
                                    title = title.Insert(openParenIndex + 2, ":");
                                }
                            }
                        }
                        
                        subTitle = targetPath;  // 경로 정보는 subTitle에 표시
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

            // 검색어가 있을 경우 파일명과 확장자를 모두 검색 (title과 subtitle도 검색 대상에 포함)
            if (!string.IsNullOrEmpty(searchTerm) &&
                !fileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !targetFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !subTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !targetPath.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new Result
            {
                Title = title,
                SubTitle = subTitle,
                IcoPath = lnkPath,
                Action = _ =>
                {
                    // 드라이브 루트 또는 폴더인 경우
                    if (isDriveRoot || isFolder)
                    {
                        context.API.OpenDirectory(targetPath);
                        return true;
                    }
                    else
                    {
                        // lnk가 가리키는 대상이 이미 따옴표로 감싸져 있다면 따옴표 제거
                        string normalizedTarget = targetPath;
                        if (normalizedTarget.StartsWith("\"") && normalizedTarget.EndsWith("\""))
                            normalizedTarget = normalizedTarget.Substring(1, normalizedTarget.Length - 2);

                        // explorer.exe로 항상 열기 (띄어쓰기/한글/따옴표 모두 안전)
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{normalizedTarget}\"",
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                        return true;
                    }
                },
                AddSelectedCount = false
            });
        }

        return results;
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

// 캐시 항목을 위한 클래스
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
}
