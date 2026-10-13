using System.Diagnostics;

namespace FloatingTransferStation.Services;

public static class StationRestart
{
    public static async Task RunAsync(string executable, int? targetPid = null)
    {
        using var current = Process.GetCurrentProcess();
        var matches = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (process.Id != current.Id && process.SessionId == current.SessionId &&
                    (!targetPid.HasValue || process.Id == targetPid.Value) &&
                    string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) { matches.Add(process); }
                else { process.Dispose(); }
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { process.Dispose(); }
        }
        // A menu shortcut has no PID. Choose only the oldest matching instance,
        // not another concurrently launched restart helper.
        var target = matches.OrderBy(process => process.StartTime).FirstOrDefault();
        foreach (var process in matches.Where(process => !ReferenceEquals(process, target))) { process.Dispose(); }
        if (target is not null)
        {
            using (target)
            {
                target.CloseMainWindow();
                var stopped = target.WaitForExitAsync();
                if (await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(2))) != stopped)
                {
                    BackupSavedMetadata();
                    if (!target.HasExited) { target.Kill(); }
                    await target.WaitForExitAsync();
                }
                else { BackupSavedMetadata(); }
            }
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
    }

    private static void BackupSavedMetadata()
    {
        var paths = AppPaths.CreateDefault();
        var destination = Path.Combine(paths.DataDirectory, "Recovery", "restart-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(destination);
        foreach (var source in new[] { paths.BoardFile, paths.SettingsFile })
        {
            if (File.Exists(source)) { File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), overwrite: false); }
        }
    }
}
