using System.Windows;
using Shell.Interfaces;
using Shell.ViewModels;

namespace Shell.Services;

public sealed class ImportReportPresenter : IImportReportPresenter
{
    public void Show(ImportReportViewModel report)
    {
        var window = new ImportReportWindow(report);
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
    }
}
