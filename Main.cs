using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentlyUsed;
using Flow.Launcher.Plugin.RecentlyUsed.Helper;
using Flow.Launcher.Plugin.RecentlyUsed.Views;
using System.Windows.Controls;
using System.IO;

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

            bool isFolder = Directory.Exists(targetPath);
            if (!settings.ShowFolders && isFolder)
                continue;

            if (!File.Exists(targetPath) && !isFolder)
                continue;

            // 대상 파일의 전체 이름 (확장자 포함)
            string targetFileName = Path.GetFileName(targetPath);
            
            // 검색어가 있을 경우 파일명과 확장자를 모두 검색
            if (!string.IsNullOrEmpty(searchTerm) &&
                !fileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                !targetFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                continue;

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
                        var driveInfo = new DriveInfo(targetPath);
                        string driveRoot = driveInfo.Name; // C:\
                        string driveLetter = driveRoot.TrimEnd('\\'); // C:
                        string label = driveInfo.VolumeLabel; 

                        title = $"{label} ({driveLetter})";
                        subTitle = driveInfo.Name; // C:\
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

            results.Add(new Result
            {
                Title = title,
                SubTitle = subTitle,
                IcoPath = lnkPath,
                Action = _ =>
                {
                    try
                    {
                        // 드라이브 루트 또는 폴더인 경우
                        if (isDriveRoot || isFolder)
                        {
                            // 디렉토리 열기
                            context.API.OpenDirectory(targetPath);
                            return true;
                        }
                        else
                        {
                            
                            context.API.ShellRun(targetPath);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        
                        if (isDriveRoot || isFolder)
                        {
                            
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = targetPath,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        else
                        {
                            try
                            {
                                
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = targetPath,
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                                return true;
                            }
                            catch (Exception pathEx)
                            {
                                try
                                {
                                    
                                    var psi = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = "cmd.exe",
                                        Arguments = $"/c start \"\" \"{targetPath}\"",
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    };
                                    System.Diagnostics.Process.Start(psi);
                                    return true;
                                }
                                catch (Exception cmdEx)
                                {
                                    
                                    context.API.ShellRun(targetPath);
                                    return true;
                                }
                            }
                        }
                        return true;
                    }
                },
                AddSelectedCount = false
            });
        }

        return results;
    }
}
