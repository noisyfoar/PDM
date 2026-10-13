using System.IO;
using System.Windows;
using Shell.Composition;
using Shell.Configuration;

namespace Shell;

public partial class App : System.Windows.Application
{
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var settings = await AppSettings.LoadAsync(settingsPath);
            _services = new AppServices(settings);
            await _services.InitializeAsync();
            MainWindow = _services.GetService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show($"Не удалось запустить Мини-PDM. Проверьте настройки и доступность базы данных.\n\n{error.Message}",
                "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _services?.Dispose(); }
        catch (Exception error)
        {
            MessageBox.Show($"Не удалось освободить ресурсы базы данных.\n\n{error.Message}",
                "Ошибка завершения работы", MessageBoxButton.OK, MessageBoxImage.Error);
            e.ApplicationExitCode = 1;
        }
        finally
        {
            _services = null;
            base.OnExit(e);
        }
    }
}
