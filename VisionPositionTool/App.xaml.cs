using System;
using System.IO;
using System.Windows;

namespace VisionPositionTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 全局异常兜底：写入错误日志便于排查（程序目录 error_log.txt）
            DispatcherUnhandledException += (s, ev) =>
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error_log.txt"), $"[{DateTime.Now:HH:mm:ss}] {ev.Exception}\r\n"); } catch { }
                MessageBox.Show("程序发生错误：\n" + ev.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ev.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error_log.txt"), $"[{DateTime.Now:HH:mm:ss}] {ev.ExceptionObject}\r\n"); } catch { }
            };
        }
    }
}
