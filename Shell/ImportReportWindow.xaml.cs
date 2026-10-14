using System.Windows;
using Shell.ViewModels;

namespace Shell;

public partial class ImportReportWindow : Window
{
    public ImportReportWindow(ImportReportViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
