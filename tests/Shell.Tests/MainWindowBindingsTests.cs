using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Application.Interfaces.Services;
using Application.Records.Objects;
using Application.Records.Composition;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class MainWindowBindingsTests
{
    [Fact]
    public async Task XamlBindsSearchCardAndStateCommandsWithoutDatabase()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var source = PresentationTraceSources.DataBindingSource;
            var previousLevel = source.Switch.Level;
            using var errors = new BindingErrorListener();
            source.Listeners.Add(errors);
            source.Switch.Level = SourceLevels.Error;
            MainWindow? window = null;
            ImportReportWindow? reportWindow = null;
            try
            {
                var service = new FakeObjectService();
                var report = new ImportCadFolderReport(
                    [new("part.m3d", "Пропущен: изменений нет.")],
                    [new("broken.m3d", "Документ повреждён.")],
                    [new("part.m3d", "У детали отсутствует масса.")]);
                var importer = new FakeImportService { Report = report };
                var reader = new FakeCompositionReader { Read = _ => Task.FromResult(service.Composition()) };
                var presenter = new FakeImportReportPresenter();
                var vm = new MainViewModel(service, importer, new FakeFolderPicker { Folder = "synthetic-import-folder" }, reader, presenter, new Application.Services.AssemblyCalculationService(reader)) { SearchText = "Деталь" };
                window = new MainWindow(vm);
                var content = Assert.IsType<Grid>(window.Content);
                Layout(content);

                vm.InitializeAsync().GetAwaiter().GetResult();
                Layout(content);
                var objects = Assert.Single(Descendants(content).OfType<TreeView>());
                var node = Assert.Single(vm.TreeRoots);
                var item = Assert.IsType<TreeViewItem>(objects.ItemContainerGenerator.ContainerFromItem(node));
                item.IsSelected = true;
                Assert.Same(node, vm.SelectedTreeNode);
                Assert.Same(node.Object, vm.SelectedObject);
                vm.OpenObjectCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);

                var versions = Assert.Single(Descendants(content).OfType<DataGrid>(), grid => ReferenceEquals(grid.ItemsSource, vm.Card!.Versions));
                Assert.Same(vm.SelectedVersion, versions.SelectedItem);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "В работе");
                var approve = Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Утвердить версию"));
                Assert.Same(vm.ApproveCommand, approve.Command);
                Assert.True(approve.IsEnabled);

                vm.ApproveCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);

                Assert.NotNull(vm.Card);
                Assert.Equal(ObjectVersionType.Approved, vm.Card.CurrentVersion!.VersionType);
                Assert.Same(vm.SelectedTreeNode, objects.SelectedItem);
                Assert.Same(vm.SelectedVersion, versions.SelectedItem);
                Assert.False(approve.IsEnabled);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "Утверждена");

                var searchBox = Assert.Single(Descendants(content).OfType<TextBox>());
                searchBox.SetCurrentValue(TextBox.TextProperty, "  ДеТаЛь  ");
                Layout(content);
                Assert.NotNull(vm.Card);
                Assert.Same(vm.SelectedTreeNode, objects.SelectedItem);
                searchBox.SetCurrentValue(TextBox.TextProperty, "Нет совпадений");
                Layout(content);
                Assert.Empty(objects.Items);
                Assert.Null(vm.Card);
                searchBox.SetCurrentValue(TextBox.TextProperty, string.Empty);
                Layout(content);
                Assert.Empty(vm.SearchText);
                Assert.Equal(2, objects.Items.Count);
                Assert.Equal(ObjectVersionType.Approved, vm.Results[0].CurrentVersionType);

                var assembly = vm.TreeRoots.Single(root => root.Object.ObjectType == ObjectType.Assembly);
                var assemblyItem = Assert.IsType<TreeViewItem>(objects.ItemContainerGenerator.ContainerFromItem(assembly));
                assemblyItem.IsExpanded = true;
                Layout(content);
                var component = Assert.Single(assembly.Children);
                Assert.False(component.IsPlaceholder);
                Assert.Equal(2, component.Quantity);
                Assert.Single(reader.Roots);
                assemblyItem.IsSelected = true;
                vm.OpenObjectCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);
                var mainTabs = Assert.Single(Descendants(content).OfType<TabControl>());
                Assert.Equal(2, mainTabs.Items.Count);
                mainTabs.SetCurrentValue(TabControl.SelectedIndexProperty, 1);
                Layout(content);
                var calculate = Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Рассчитать"));
                Assert.Same(vm.CalculateCommand, calculate.Command);
                Assert.True(calculate.IsEnabled);
                vm.CalculateCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);
                Assert.Equal(4m, vm.Calculation!.MassKg);
                var specification = Assert.Single(Descendants(content).OfType<DataGrid>(), grid => ReferenceEquals(grid.ItemsSource, vm.Calculation.Specification));
                Assert.Single(specification.Items);
                Assert.All(specification.Columns, column => Assert.True(column.ActualWidth >= column.MinWidth));
                var specificationScroll = Assert.Single(Descendants(specification).OfType<ScrollViewer>());
                Assert.True(specificationScroll.ScrollableWidth > 0);
                specificationScroll.ScrollToRightEnd();
                Layout(content);
                Assert.Equal(specificationScroll.ScrollableWidth, specificationScroll.HorizontalOffset);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == vm.CalculationDescription);
                reader.Read = _ => Task.FromResult<IReadOnlyList<CompositionEntry>>(service.Composition()
                    .Select(entry => entry.ObjectType == ObjectType.Part ? entry with { MassKg = null } : entry).ToArray());
                vm.CalculateCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);
                Assert.Null(vm.Calculation!.MassKg);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == vm.Calculation.Issues[0].Message);
                reader.Read = _ => Task.FromResult(service.Composition());
                var componentItem = Assert.IsType<TreeViewItem>(assemblyItem.ItemContainerGenerator.ContainerFromItem(component));
                componentItem.IsSelected = true;
                vm.OpenObjectCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);
                Assert.Equal(component.Object.Id, vm.Card!.Id);
                Assert.Equal(0, mainTabs.SelectedIndex);
                Assert.Same(component, vm.SelectedTreeNode);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text == "×2");
                searchBox.SetCurrentValue(TextBox.TextProperty, "Сборка");
                Layout(content);
                Assert.Single(objects.Items);
                Assert.Same(vm.SelectedTreeNode, objects.SelectedItem);
                Assert.Equal(component.Object.Id, vm.Card!.Id);

                Layout(content, 940, 560);
                var cardScroll = Assert.Single(Descendants(content).OfType<ScrollViewer>(), scroll => scroll.Name == "CardScroll");
                Assert.True(cardScroll.ScrollableHeight > 0);
                cardScroll.ScrollToEnd();
                Layout(content, 940, 560);
                var annul = Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Аннулировать версию"));
                var buttonPosition = annul.TransformToAncestor(cardScroll).Transform(new Point(0, 0));
                Assert.True(buttonPosition.Y >= 0 && buttonPosition.Y + annul.ActualHeight <= cardScroll.ActualHeight);
                Layout(content);
                cardScroll.ScrollToHome();
                Layout(content);

                var importButton = Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Импортировать папку…"));
                Assert.Same(vm.ImportCommand, importButton.Command);
                var reportButton = Assert.Single(Descendants(content).OfType<Button>(), button => Equals(button.Content, "Отчёт импорта"));
                Assert.Same(vm.ShowImportReportCommand, reportButton.Command);
                Assert.False(reportButton.IsEnabled);
                vm.ImportCommand.ExecuteAsync().GetAwaiter().GetResult();
                Layout(content);
                Assert.Single(Descendants(content).OfType<TabControl>());
                Assert.True(reportButton.IsEnabled);
                var reportModel = Assert.Single(presenter.Reports);
                reportWindow = new ImportReportWindow(reportModel);
                Assert.Same(reportModel, reportWindow.DataContext);
                var reportContent = Assert.IsType<Grid>(reportWindow.Content);
                Layout(reportContent, 920, 540);
                var accepted = Assert.Single(Descendants(reportContent).OfType<DataGrid>(), grid => ReferenceEquals(grid.ItemsSource, reportModel.Report.Accepted));
                Assert.Equal(report.Accepted[0], Assert.Single(accepted.Items.Cast<ImportAcceptedFile>()));
                Assert.Contains(Descendants(reportContent).OfType<TextBlock>(), text => text.Text == reportModel.Summary);
                Assert.Contains(Descendants(reportContent).OfType<TextBlock>(), text => text.Text == reportModel.FolderPath);

                var reportTabs = Assert.Single(reportContent.Children.OfType<TabControl>());
                reportTabs.SelectedIndex = 1;
                Layout(reportContent, 920, 540);
                var rejected = Assert.Single(Descendants(reportContent).OfType<DataGrid>(), grid => ReferenceEquals(grid.ItemsSource, reportModel.Report.Rejected));
                Assert.Equal(report.Rejected[0], Assert.Single(rejected.Items.Cast<ImportRejectedFile>()));
                Assert.Contains(Descendants(reportContent).OfType<TextBlock>(), text => text.Text == report.Rejected[0].Reason);
                reportTabs.SelectedIndex = 2;
                Layout(reportContent, 920, 540);
                var warnings = Assert.Single(Descendants(reportContent).OfType<DataGrid>(), grid => ReferenceEquals(grid.ItemsSource, reportModel.Report.Warnings));
                Assert.Equal(report.Warnings[0], Assert.Single(warnings.Items.Cast<ImportWarning>()));
                Assert.Contains(Descendants(reportContent).OfType<TextBlock>(), text => text.Text == report.Warnings[0].Message);
                Assert.True(errors.Messages.Length == 0, errors.Messages.ToString());
                completion.SetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally
            {
                reportWindow?.Close();
                window?.Close();
                source.Switch.Level = previousLevel;
                source.Listeners.Remove(errors);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static void Layout(FrameworkElement content, double width = 1120, double height = 700)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }

    private sealed class FakeObjectService : IPdmObjectService
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly Guid _assemblyId = Guid.NewGuid();
        private readonly PdmVersionDetails _assemblyVersion = new(Guid.NewGuid(), 1, ObjectVersionType.InWork,
            "Сборка", null, null, DateTime.UtcNow);
        private PdmVersionDetails _version = new(Guid.NewGuid(), 1, ObjectVersionType.InWork,
            "Деталь", "Сталь", 2m, DateTime.UtcNow);
        private PdmObjectDetails Card => new(_id, ObjectType.Part, "АБВГ.123456.001", _version.Name, _version, [_version]);
        private PdmObjectDetails AssemblyCard => new(_assemblyId, ObjectType.Assembly, "АБВГ.123456.002", "Сборка", _assemblyVersion, [_assemblyVersion]);

        public IReadOnlyList<CompositionEntry> Composition() => [
            new(_assemblyId, null, _assemblyVersion.Id, ObjectType.Assembly, AssemblyCard.Designation,
                "Сборка", _assemblyVersion.VersionType, null, null, 1, 1, 0, [_assemblyId], false, 1),
            new(_id, _assemblyId, _version.Id, ObjectType.Part, Card.Designation, Card.Name,
                _version.VersionType, _version.Material, _version.MassKg, 2, 2, 1, [_assemblyId, _id], false, 1)];

        public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PdmObjectSummary>>([new(_id, ObjectType.Part, Card.Designation,
                _version.Name, _version.Id, 1, _version.VersionType),
                new(_assemblyId, ObjectType.Assembly, AssemblyCard.Designation, "Сборка", _assemblyVersion.Id, 1, _assemblyVersion.VersionType)]);

        public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default) =>
            Task.FromResult(objectId == _assemblyId ? AssemblyCard : Card);

        public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default)
        {
            _version = _version with { VersionType = ObjectVersionType.Approved };
            return Task.FromResult(Card);
        }

        public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
