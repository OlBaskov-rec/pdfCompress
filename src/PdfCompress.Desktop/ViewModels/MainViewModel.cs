using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfCompress.Core.Models;
using PdfCompress.Core.Services;
using PdfCompress.Desktop.Services;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>
/// Состояние главного окна: папки-источники, список найденных PDF, параметры сжатия и результаты.
///
/// Два способа задать сжатие (степень и предельный размер) взаимоисключающие: активен ровно один,
/// второй блок в это время выключен — см. <see cref="UseLevelMode"/>. У предельного размера есть
/// ещё и область действия: файл, папка целиком или каждая подпапка (<see cref="Scope"/>).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// Имя вложенной папки, куда по умолчанию складываются результаты. Название на языке
    /// интерфейса системы: русскоязычному пользователю «сжатые» понятнее, чем «compressed»,
    /// а искать папку он будет глазами в общем списке.
    /// </summary>
    private static string DefaultOutputFolderName =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "сжатые" : "compressed";

    private readonly IFolderPicker _folderPicker;
    private readonly PdfFileScanner _scanner = new();
    private readonly PdfCompressionService _compressor = new();
    private readonly UpdateService _updateService = new();

    /// <summary>
    /// Результаты сканирования по каждой папке. Держим их, чтобы снятие флажка с папки не
    /// требовало повторного обхода: на сетевых шарах это заметные секунды.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<PdfFileEntry>> _scanned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Пользователь сам выбрал папку результатов — тогда раскладываем всё туда, а не рядом
    /// с каждым источником.
    /// </summary>
    private bool _outputFolderChosenByUser;

    private CancellationTokenSource? _cancellation;

    public MainViewModel(IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;

        Files.CollectionChanged += OnFilesCollectionChanged;
        Folders.CollectionChanged += OnFoldersCollectionChanged;

        var settings = SettingsService.Load();
        includeSubfolders = settings.IncludeSubfolders;
        useLevelMode = settings.UseLevelMode;
        levelValue = Math.Clamp(settings.Level, 1, 5);
        maxSizeValue = settings.MaxSizeValue > 0 ? (decimal)settings.MaxSizeValue : 5m;
        selectedUnit = SizeUnitOption.All.FirstOrDefault(u => u.Unit == settings.MaxSizeUnit) ?? SizeUnitOption.All[2];
        outputFolder = settings.OutputFolder;
        _outputFolderChosenByUser = settings.OutputFolderIsCustom;

        limitPerFolder = settings.Scope == TargetScope.Folder;
        limitPerSubfolder = settings.Scope == TargetScope.Subfolder;

        foreach (var path in settings.SourceFolders ?? new List<string>())
            AddFolderItem(path, selected: true);

        // Совместимость с настройками прежних версий, где папка была одна.
        if (Folders.Count == 0 && !string.IsNullOrWhiteSpace(settings.SourceFolder))
            AddFolderItem(settings.SourceFolder, selected: true);

        if (Folders.Count > 0)
            Rescan();
    }

    // ── Папки-источники ────────────────────────────────────────────────────────────────────

    /// <summary>Папки, в которых ищем PDF. Панель слева.</summary>
    public ObservableCollection<SourceFolderItem> Folders { get; } = new();

    [ObservableProperty]
    private bool includeSubfolders;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFolderCommand))]
    private SourceFolderItem? selectedFolder;

    private int SelectedFolderCount => Folders.Count(f => f.IsSelected && f.IsAvailable);

    // ── Найденные файлы ────────────────────────────────────────────────────────────────────

    /// <summary>Найденные PDF-файлы отмеченных папок.</summary>
    public ObservableCollection<PdfFileItem> Files { get; } = new();

    [ObservableProperty]
    private string filesSummary = "Папки не выбраны";

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

    // ── Область действия предела ───────────────────────────────────────────────────────────

    /// <summary>
    /// Предел относится ко всей папке целиком. Взаимоисключающе с
    /// <see cref="LimitPerSubfolder"/>: включение одного снимает другой.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSizeHint))]
    [NotifyPropertyChangedFor(nameof(ScopeHint))]
    private bool limitPerFolder;

    /// <summary>Предел относится к каждой подпапке первого уровня по отдельности.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSizeHint))]
    [NotifyPropertyChangedFor(nameof(ScopeHint))]
    private bool limitPerSubfolder;

    partial void OnLimitPerFolderChanged(bool value)
    {
        if (value) LimitPerSubfolder = false;
    }

    partial void OnLimitPerSubfolderChanged(bool value)
    {
        if (value) LimitPerFolder = false;
    }

    /// <summary>К чему применяется предел прямо сейчас.</summary>
    public TargetScope Scope =>
        LimitPerFolder ? TargetScope.Folder :
        LimitPerSubfolder ? TargetScope.Subfolder :
        TargetScope.File;

    public string TargetSizeHint
    {
        get
        {
            if (TargetBytes <= 0)
                return "укажите размер больше нуля";

            string limit = $"{TargetBytes:N0} Б";
            return Scope switch
            {
                TargetScope.Folder => $"сумма по каждой папке не превысит {limit}",
                TargetScope.Subfolder => $"сумма по каждой подпапке не превысит {limit}",
                _ => $"ни один файл не будет больше {limit}",
            };
        }
    }

    /// <summary>Пояснение к выбранной области действия — что именно будет происходить.</summary>
    public string ScopeHint => Scope switch
    {
        TargetScope.Folder =>
            "Степень подбирается одна на всю папку, чтобы её общий вес уложился в предел. " +
            "Это дольше: по папке делается несколько проходов.",
        TargetScope.Subfolder =>
            "Каждая подпапка первого уровня укладывается в предел отдельно, со своей степенью сжатия. " +
            "Нужен поиск во вложенных папках.",
        _ =>
            "Степень подбирается сама — самая щадящая из тех, что укладываются в предел.",
    };

    // ── Результаты ─────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private string? outputFolder;

    /// <summary>
    /// Что показывать в поле «Папка для результатов». Когда папку не выбирали вручную,
    /// результаты ложатся рядом с каждым источником — одним путём это не выразить.
    /// </summary>
    public string OutputFolderDisplay
    {
        get
        {
            if (_outputFolderChosenByUser && !string.IsNullOrWhiteSpace(OutputFolder))
                return OutputFolder!;

            var selected = Folders.Where(f => f.IsSelected).ToList();
            if (selected.Count == 1)
                return Path.Combine(selected[0].Path, DefaultOutputFolderName);

            return selected.Count == 0
                ? $"подпапка «{DefaultOutputFolderName}» в каждой выбранной папке"
                : $"подпапка «{DefaultOutputFolderName}» в каждой из {selected.Count} папок";
        }
    }

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
    private string statusText = "Добавьте папку с PDF-файлами.";

    /// <summary>Текст последней ошибки уровня всей операции; пустая строка — ошибки нет.</summary>
    [ObservableProperty]
    private string errorText = string.Empty;

    // ── Команды ────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        string? start = Folders.LastOrDefault()?.Path;
        string? folder = await _folderPicker.PickFolderAsync("Добавьте папку с PDF-файлами", start);
        if (folder is null) return;

        if (!AddFolderItem(folder, selected: true))
        {
            StatusText = "Эта папка уже в списке.";
            return;
        }

        Rescan();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFolder))]
    private void RemoveFolder()
    {
        var item = SelectedFolder;
        if (item is null) return;

        item.PropertyChanged -= OnFolderItemPropertyChanged;
        Folders.Remove(item);
        _scanned.Remove(item.Path);
        SelectedFolder = null;

        RebuildFileList();
        NotifyOutputChanged();
    }

    private bool CanRemoveFolder() => SelectedFolder is not null && !IsBusy;

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        string? start = _outputFolderChosenByUser ? OutputFolder : Folders.FirstOrDefault()?.Path;
        string? folder = await _folderPicker.PickFolderAsync("Куда сохранять сжатые файлы", start);
        if (folder is null) return;

        OutputFolder = folder;
        _outputFolderChosenByUser = true;
        NotifyOutputChanged();
        RebuildFileList();
    }

    /// <summary>Вернуться к раскладке «рядом с каждой папкой-источником».</summary>
    [RelayCommand]
    private void UseDefaultOutput()
    {
        _outputFolderChosenByUser = false;
        OutputFolder = null;
        NotifyOutputChanged();
        RebuildFileList();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void Refresh() => Rescan();

    private bool CanRefresh() => Folders.Count > 0 && !IsBusy;

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void ClearSelection() => SetAllSelected(false);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task ProcessAsync()
    {
        var selected = Files.Where(f => f.IsSelected).Select(f => f.Entry).ToList();
        if (selected.Count == 0) return;

        if (TryFindOutputInsideSource(out string clash))
        {
            ErrorText = $"Папка результатов совпадает с папкой-источником «{clash}» — исходные файлы " +
                        "были бы перезаписаны. Выберите другую папку.";
            return;
        }

        ErrorText = string.Empty;
        Results.Clear();
        ResultsSummary = string.Empty;
        Progress = 0;
        IsBusy = true;

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        try
        {
            if (UseLevelMode || Scope == TargetScope.File)
                await ProcessPerFileAsync(selected, token);
            else
                await ProcessByGroupsAsync(selected, token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Обработка отменена.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка пакетной обработки", ex);
            ErrorText = $"Обработка прервана: {ex.Message}";
            StatusText = "Обработка прервана из-за ошибки.";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsBusy = false;
            RefreshCommand.NotifyCanExecuteChanged();
            RemoveFolderCommand.NotifyCanExecuteChanged();
            SaveSettings();
        }
    }

    /// <summary>Обычный режим: каждый файл обрабатывается сам по себе.</summary>
    private async Task ProcessPerFileAsync(List<PdfFileEntry> selected, CancellationToken token)
    {
        var request = UseLevelMode
            ? CompressionRequest.ForLevel(Level)
            : CompressionRequest.ForTargetSize(TargetBytes);

        long totalBefore = 0, totalAfter = 0;
        int done = 0, failed = 0;

        try
        {
            foreach (var entry in selected)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"Обработка {done + 1} из {selected.Count}: {entry.DisplayName}";

                string output = OutputPathFor(entry);
                var result = await Task.Run(
                    () => _compressor.Compress(entry.FullPath, output, request, token), token);

                Results.Add(new ResultRow(result));
                if (result.Outcome == CompressionOutcome.Failed)
                {
                    failed++;
                    AppLog.Error($"Не удалось обработать «{entry.DisplayName}»: {result.Error}");
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
            throw;
        }
        finally
        {
            ResultsSummary = BuildSummary(totalBefore, totalAfter);
        }
    }

    /// <summary>
    /// Предел на папку или на подпапку: файлы бьются на группы, и для каждой подбирается
    /// одна степень сжатия, при которой в предел укладывается их сумма.
    /// </summary>
    private async Task ProcessByGroupsAsync(List<PdfFileEntry> selected, CancellationToken token)
    {
        var groups = FileGrouping.Build(selected, Scope);
        long target = TargetBytes;

        long totalBefore = 0, totalAfter = 0;
        int groupsDone = 0, missed = 0;

        // Progress создаётся на UI-потоке, поэтому обратные вызовы приходят тоже на него.
        int capturedGroupsDone = 0;
        var reporter = new Progress<GroupProgress>(p =>
        {
            double within = p.TotalPasses <= 0 || p.FileCount <= 0
                ? 0
                : (p.Pass - 1 + (double)p.FileIndex / p.FileCount) / p.TotalPasses;
            Progress = 100.0 * (capturedGroupsDone + Math.Clamp(within, 0, 1)) / groups.Count;
        });

        try
        {
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                capturedGroupsDone = groupsDone;
                StatusText = $"Подбор под предел: {group.Title} " +
                             $"({groupsDone + 1} из {groups.Count}, файлов — {group.Files.Count})";

                var jobs = group.Files
                    .Select(f => new CompressionJob(f.FullPath, OutputPathFor(f), f.DisplayName))
                    .ToList();

                var result = await Task.Run(
                    () => _compressor.CompressGroupToTargetSize(jobs, target, reporter, token), token);

                Results.Add(ResultRow.ForGroup(group, result, target));
                foreach (var file in result.Files)
                    Results.Add(new ResultRow(file));

                totalBefore += result.OriginalBytes;
                totalAfter += result.ResultBytes;
                if (!result.TargetReached) missed++;

                groupsDone++;
                Progress = 100.0 * groupsDone / groups.Count;
            }

            StatusText = missed == 0
                ? $"Готово: папок обработано — {groupsDone}, все уложились в предел."
                : $"Готово: папок обработано — {groupsDone}, не уложились в предел — {missed}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Отменено. Успели обработать папок: {groupsDone} из {groups.Count}.";
            throw;
        }
        finally
        {
            ResultsSummary = BuildSummary(totalBefore, totalAfter);
        }
    }

    private bool CanProcess() =>
        !IsBusy
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
    private void OpenOutputFolder()
    {
        string? folder = _outputFolderChosenByUser
            ? OutputFolder
            : Folders.FirstOrDefault(f => f.IsSelected) is { } first
                ? Path.Combine(first.Path, DefaultOutputFolderName)
                : null;

        if (!string.IsNullOrWhiteSpace(folder))
            PlatformHelper.OpenFolder(folder!);
    }

    private bool CanOpenOutputFolder() => Folders.Count > 0 || !string.IsNullOrWhiteSpace(OutputFolder);

    // ── Внутреннее ─────────────────────────────────────────────────────────────────────────

    /// <summary>Добавляет папку в список. Возвращает false, если она там уже есть.</summary>
    private bool AddFolderItem(string path, bool selected)
    {
        string normalized = Path.TrimEndingDirectorySeparator(path);
        if (Folders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            return false;

        var item = new SourceFolderItem(normalized, selected);
        item.PropertyChanged += OnFolderItemPropertyChanged;
        Folders.Add(item);
        return true;
    }

    /// <summary>Перечитывает все папки списка и заново наполняет список файлов.</summary>
    private void Rescan()
    {
        _scanned.Clear();
        var problems = new List<string>();

        foreach (var folder in Folders)
        {
            try
            {
                var found = _scanner.Scan(folder.Path, IncludeSubfolders);
                _scanned[folder.Path] = found;
                folder.IsAvailable = true;
                folder.FileCount = found.Count;
                folder.TotalBytes = found.Sum(f => f.SizeBytes);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Не удалось прочитать папку «{folder.Path}»", ex);
                _scanned[folder.Path] = Array.Empty<PdfFileEntry>();
                folder.IsAvailable = false;
                folder.FileCount = 0;
                folder.TotalBytes = 0;
                problems.Add(folder.Name);
            }
        }

        ErrorText = problems.Count == 0
            ? string.Empty
            : $"Не удалось прочитать папки: {string.Join(", ", problems)}.";

        RebuildFileList();
    }

    /// <summary>
    /// Собирает список файлов из уже просканированных папок. Повторно диск не читает —
    /// используется при снятии/установке флажков и смене папки результатов.
    /// </summary>
    private void RebuildFileList()
    {
        Files.CollectionChanged -= OnFilesCollectionChanged;
        foreach (var item in Files)
            item.PropertyChanged -= OnFileItemPropertyChanged;
        Files.Clear();
        Files.CollectionChanged += OnFilesCollectionChanged;

        long total = 0;
        foreach (var folder in Folders)
        {
            if (!folder.IsSelected || !_scanned.TryGetValue(folder.Path, out var entries))
                continue;

            foreach (var entry in entries)
            {
                // Результаты прошлого запуска лежат внутри исходной папки — при поиске во
                // вложенных папках они попали бы в список и сжимались бы повторно.
                if (IsInsideOutput(entry))
                    continue;

                var item = new PdfFileItem(entry);
                item.PropertyChanged += OnFileItemPropertyChanged;
                Files.Add(item);
                total += entry.SizeBytes;
            }
        }

        FilesSummary = Folders.Count == 0
            ? "Папки не выбраны"
            : Files.Count == 0
                ? "PDF-файлов не найдено"
                : $"Найдено файлов: {Files.Count} · {SizeUnits.Format(total)}";

        if (!IsBusy)
        {
            StatusText = Files.Count == 0
                ? "Добавьте папку или включите поиск во вложенных папках."
                : "Задайте параметры сжатия и нажмите «Обработать».";
        }

        NotifyOutputChanged();
        ProcessCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Лежит ли файл внутри папки, куда мы пишем результаты для его источника.</summary>
    private bool IsInsideOutput(PdfFileEntry entry)
    {
        string outputRoot = OutputRootFor(entry.RootFolder);
        string full = Path.GetFullPath(entry.FullPath);
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot)) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Куда складывать результаты для файлов указанной папки-источника.</summary>
    private string OutputRootFor(string sourceFolder)
    {
        if (!_outputFolderChosenByUser || string.IsNullOrWhiteSpace(OutputFolder))
            return Path.Combine(sourceFolder, DefaultOutputFolderName);

        // Несколько источников в одну папку — раскладываем по именам источников, иначе
        // одноимённые файлы из разных папок затирали бы друг друга.
        return SelectedFolderCount > 1
            ? Path.Combine(OutputFolder!, FolderNameOf(sourceFolder))
            : OutputFolder!;
    }

    /// <summary>Полный путь результата для файла, с сохранением структуры подпапок.</summary>
    private string OutputPathFor(PdfFileEntry entry) =>
        Path.Combine(OutputRootFor(entry.RootFolder), entry.RelativePath);

    private static string FolderNameOf(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? "диск" : name;
    }

    /// <summary>
    /// Проверяет, не совпадает ли выбранная вручную папка результатов с какой-нибудь
    /// папкой-источником: писать туда — значит затереть оригиналы.
    /// </summary>
    private bool TryFindOutputInsideSource(out string folderName)
    {
        folderName = string.Empty;
        if (!_outputFolderChosenByUser || string.IsNullOrWhiteSpace(OutputFolder))
            return false;

        foreach (var folder in Folders.Where(f => f.IsSelected))
        {
            if (IsSameFolder(OutputFolder, folder.Path))
            {
                folderName = folder.Name;
                return true;
            }
        }
        return false;
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var file in Files)
            file.IsSelected = selected;
    }

    private void OnFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ProcessCommand.NotifyCanExecuteChanged();

    private void OnFoldersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
        NotifyOutputChanged();
    }

    private void OnFileItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfFileItem.IsSelected))
            ProcessCommand.NotifyCanExecuteChanged();
    }

    private void OnFolderItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceFolderItem.IsSelected))
            RebuildFileList();
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (Folders.Count > 0)
            Rescan();
    }

    private void NotifyOutputChanged()
    {
        OnPropertyChanged(nameof(OutputFolderDisplay));
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
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
        SourceFolders = Folders.Select(f => f.Path).ToList(),
        OutputFolder = OutputFolder,
        IncludeSubfolders = IncludeSubfolders,
        UseLevelMode = UseLevelMode,
        Level = (int)Level,
        MaxSizeValue = (double)MaxSizeValue,
        MaxSizeUnit = SelectedUnit.Unit,
        OutputFolderIsCustom = _outputFolderChosenByUser,
        Scope = Scope,
    });
}
