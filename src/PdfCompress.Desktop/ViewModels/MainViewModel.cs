using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfCompress.Core.Models;
using PdfCompress.Core.Services;
using PdfCompress.Desktop.Services;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>
/// Состояние главного окна: выбранная папка, список найденных PDF, параметры сжатия и результаты.
///
/// Два способа задать сжатие (степень и предельный размер) взаимоисключающие: активен ровно один,
/// второй блок в это время выключен — см. <see cref="UseLevelMode"/>.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>Имя подпапки, куда по умолчанию складываются результаты.</summary>
    private const string DefaultOutputFolderName = "compressed";

    private readonly IFolderPicker _folderPicker;
    private readonly PdfFileScanner _scanner = new();
    private readonly PdfCompressionService _compressor = new();

    /// <summary>
    /// Пользователь сам выбрал папку результатов — тогда при смене исходной папки её не трогаем.
    /// </summary>
    private bool _outputFolderChosenByUser;

    private CancellationTokenSource? _cancellation;

    public MainViewModel(IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;

        Files.CollectionChanged += OnFilesCollectionChanged;

        var settings = SettingsService.Load();
        includeSubfolders = settings.IncludeSubfolders;
        useLevelMode = settings.UseLevelMode;
        levelValue = Math.Clamp(settings.Level, 1, 5);
        maxSizeValue = settings.MaxSizeValue > 0 ? (decimal)settings.MaxSizeValue : 5m;
        selectedUnit = SizeUnitOption.All.FirstOrDefault(u => u.Unit == settings.MaxSizeUnit) ?? SizeUnitOption.All[2];
        outputFolder = settings.OutputFolder;
        _outputFolderChosenByUser = !string.IsNullOrWhiteSpace(settings.OutputFolder);

        if (!string.IsNullOrWhiteSpace(settings.SourceFolder) && Directory.Exists(settings.SourceFolder))
        {
            sourceFolder = settings.SourceFolder;
            Rescan();
        }
    }

    // ── Источник ───────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private string? sourceFolder;

    [ObservableProperty]
    private bool includeSubfolders;

    /// <summary>Найденные PDF-файлы; порядок — как вернул сканер (по алфавиту).</summary>
    public ObservableCollection<PdfFileItem> Files { get; } = new();

    [ObservableProperty]
    private string filesSummary = "Папка не выбрана";

    // ── Режим сжатия ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// true — работает выбор степени сжатия, блок предельного размера выключен;
    /// false — наоборот. Ровно одно из двух, третьего состояния нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseTargetSizeMode))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private bool useLevelMode = true;

    public bool UseTargetSizeMode
    {
        get => !UseLevelMode;
        set => UseLevelMode = !value;
    }

    /// <summary>Положение бегунка степеней: 1 (минимальное) … 5 (максимальное).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelName))]
    [NotifyPropertyChangedFor(nameof(LevelDetails))]
    private double levelValue = 3;

    public CompressionLevel Level => (CompressionLevel)Math.Clamp((int)Math.Round(LevelValue), 1, 5);

    public string LevelName => Level switch
    {
        CompressionLevel.Minimal => "Минимальное",
        CompressionLevel.Light => "Слабое",
        CompressionLevel.Medium => "Среднее",
        CompressionLevel.Strong => "Сильное",
        _ => "Максимальное",
    };

    /// <summary>Подпись под бегунком: что именно означает выбранная степень.</summary>
    public string LevelDetails
    {
        get
        {
            var options = CompressionOptions.ForLevel(Level);
            string purpose = Level switch
            {
                CompressionLevel.Minimal => "почти без потери качества",
                CompressionLevel.Light => "качество печати",
                CompressionLevel.Medium => "для документооборота",
                CompressionLevel.Strong => "для чтения с экрана",
                _ => "минимальный размер",
            };
            return $"{options.TargetDpi} dpi · JPEG {options.JpegQuality} — {purpose}";
        }
    }

    /// <summary>Число в поле предельного размера.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSizeHint))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private decimal maxSizeValue = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSizeHint))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private SizeUnitOption selectedUnit = SizeUnitOption.All[2];

    public IReadOnlyList<SizeUnitOption> Units => SizeUnitOption.All;

    /// <summary>Введённый предел в байтах; 0 — значение некорректно.</summary>
    private long TargetBytes =>
        SizeUnits.TryToBytes((double)MaxSizeValue, SelectedUnit.Unit, out long bytes) ? bytes : 0;

    public string TargetSizeHint => TargetBytes > 0
        ? $"каждый файл будет ужат до {SizeUnits.Format(TargetBytes)} или меньше"
        : "укажите размер больше нуля";

    // ── Результаты ─────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private string? outputFolder;

    public ObservableCollection<ResultRow> Results { get; } = new();

    [ObservableProperty]
    private string resultsSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string statusText = "Выберите папку с PDF-файлами.";

    /// <summary>Текст последней ошибки уровня всей операции; пустая строка — ошибки нет.</summary>
    [ObservableProperty]
    private string errorText = string.Empty;

    // ── Команды ────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Выберите папку с PDF-файлами", SourceFolder);
        if (folder is null) return;

        SourceFolder = folder;
        if (!_outputFolderChosenByUser)
            OutputFolder = Path.Combine(folder, DefaultOutputFolderName);

        Rescan();
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Куда сохранять сжатые файлы", OutputFolder ?? SourceFolder);
        if (folder is null) return;

        OutputFolder = folder;
        _outputFolderChosenByUser = true;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void Refresh() => Rescan();

    private bool CanRefresh() => !string.IsNullOrWhiteSpace(SourceFolder);

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void ClearSelection() => SetAllSelected(false);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task ProcessAsync()
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        string destination = OutputFolder!;
        if (IsSameFolder(destination, SourceFolder))
        {
            ErrorText = "Папка результатов совпадает с исходной — исходные файлы были бы перезаписаны. " +
                        "Выберите другую папку.";
            return;
        }

        var request = UseLevelMode
            ? CompressionRequest.ForLevel(Level)
            : CompressionRequest.ForTargetSize(TargetBytes);

        ErrorText = string.Empty;
        Results.Clear();
        ResultsSummary = string.Empty;
        Progress = 0;
        IsBusy = true;

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        long totalBefore = 0, totalAfter = 0;
        int done = 0, failed = 0;

        try
        {
            foreach (var file in selected)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"Обработка {done + 1} из {selected.Count}: {file.FileName}";

                string output = PdfCompressionService.BuildOutputPath(file.FullPath, destination);
                var result = await Task.Run(
                    () => _compressor.Compress(file.FullPath, output, request, token), token);

                Results.Add(new ResultRow(result));
                if (result.Outcome == CompressionOutcome.Failed)
                {
                    failed++;
                    AppLog.Error($"Не удалось обработать «{file.FileName}»: {result.Error}");
                }
                else
                {
                    totalBefore += result.OriginalBytes;
                    totalAfter += result.ResultBytes;
                }

                done++;
                Progress = 100.0 * done / selected.Count;
            }

            StatusText = failed == 0
                ? $"Готово: обработано файлов — {done}."
                : $"Готово: обработано файлов — {done}, с ошибками — {failed}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Отменено. Успели обработать: {done} из {selected.Count}.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка пакетной обработки", ex);
            ErrorText = $"Обработка прервана: {ex.Message}";
            StatusText = "Обработка прервана из-за ошибки.";
        }
        finally
        {
            ResultsSummary = BuildSummary(totalBefore, totalAfter);
            _cancellation?.Dispose();
            _cancellation = null;
            IsBusy = false;
            SaveSettings();
        }
    }

    private bool CanProcess() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && Files.Any(f => f.IsSelected)
        && (UseLevelMode || TargetBytes > 0);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        StatusText = "Отмена…";
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenOutputFolder))]
    private void OpenOutputFolder() => PlatformHelper.OpenFolder(OutputFolder!);

    private bool CanOpenOutputFolder() =>
        !string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder);

    // ── Внутреннее ─────────────────────────────────────────────────────────────────────────

    /// <summary>Перечитывает папку и заново наполняет список файлов.</summary>
    private void Rescan()
    {
        Files.CollectionChanged -= OnFilesCollectionChanged;
        foreach (var item in Files)
            item.PropertyChanged -= OnFileItemPropertyChanged;
        Files.Clear();
        Files.CollectionChanged += OnFilesCollectionChanged;

        if (string.IsNullOrWhiteSpace(SourceFolder))
        {
            FilesSummary = "Папка не выбрана";
            ProcessCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            var found = _scanner.Scan(SourceFolder, IncludeSubfolders);
            foreach (var entry in found)
            {
                var item = new PdfFileItem(entry);
                item.PropertyChanged += OnFileItemPropertyChanged;
                Files.Add(item);
            }

            long total = found.Sum(f => f.SizeBytes);
            FilesSummary = found.Count == 0
                ? "PDF-файлов в папке не найдено"
                : $"Найдено файлов: {found.Count} · {SizeUnits.Format(total)}";
            StatusText = found.Count == 0
                ? "Выберите другую папку или включите поиск во вложенных папках."
                : "Задайте параметры сжатия и нажмите «Обработать».";
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось прочитать папку «{SourceFolder}»", ex);
            FilesSummary = "Папку прочитать не удалось";
            ErrorText = ex.Message;
        }

        ProcessCommand.NotifyCanExecuteChanged();
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var file in Files)
            file.IsSelected = selected;
    }

    private void OnFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ProcessCommand.NotifyCanExecuteChanged();

    private void OnFileItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfFileItem.IsSelected))
            ProcessCommand.NotifyCanExecuteChanged();
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(SourceFolder))
            Rescan();
    }

    private static string BuildSummary(long before, long after)
    {
        if (before <= 0)
            return string.Empty;

        double percent = 100.0 * (before - after) / before;
        return $"Итого: {SizeUnits.Format(before)} → {SizeUnits.Format(after)}   " +
               $"(экономия {SizeUnits.Format(Math.Max(0, before - after))}, {percent:0.#} %)";
    }

    /// <summary>Сравнивает пути папок с поправкой на регистр и завершающий разделитель.</summary>
    private static bool IsSameFolder(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        try
        {
            string left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
            string right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
            // Windows и macOS не различают регистр в путях; в Linux сравнение по регистру строгое.
            var comparison = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(left, right, comparison);
        }
        catch (Exception)
        {
            // Некорректный путь сравнивать нечего — пусть решает попытка записи.
            return false;
        }
    }

    /// <summary>Сохраняет выбор пользователя, чтобы следующий запуск открылся в том же состоянии.</summary>
    public void SaveSettings() => SettingsService.Save(new AppSettings
    {
        SourceFolder = SourceFolder,
        OutputFolder = OutputFolder,
        IncludeSubfolders = IncludeSubfolders,
        UseLevelMode = UseLevelMode,
        Level = (int)Level,
        MaxSizeValue = (double)MaxSizeValue,
        MaxSizeUnit = SelectedUnit.Unit,
    });
}
