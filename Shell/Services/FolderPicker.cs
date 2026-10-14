using Microsoft.Win32;
using Shell.Interfaces;

namespace Shell.Services;

public sealed class FolderPicker : IFolderPicker
{
    public string? SelectFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для импорта CAD-документов",
            Multiselect = false
        };
        var owner = System.Windows.Application.Current?.MainWindow;
        var confirmed = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return confirmed == true ? dialog.FolderName : null;
    }
}
