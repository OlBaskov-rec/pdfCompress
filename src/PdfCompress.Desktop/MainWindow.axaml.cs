using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PdfCompress.Desktop.Services;
using PdfCompress.Desktop.ViewModels;

namespace PdfCompress.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new StorageProviderFolderPicker(() => this));
        DataContext = _viewModel;

        Title = $"PDF Compress {AppInfo.Version}";

        var geometry = WindowStateService.Load();
        if (geometry is not null)
        {
            Width = geometry.Width;
            Height = geometry.Height;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        // Размер окна и настройки сохраняем при закрытии: в процессе работы это лишние записи.
        WindowStateService.Save(Width, Height);
        _viewModel.SaveSettings();
        base.OnUnloaded(e);
    }
}
