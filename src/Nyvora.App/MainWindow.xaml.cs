using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nyvora.App.Services;
using Nyvora.App.Windowing;
using Nyvora.Core.Animation;
using Nyvora.Core.State;
using Nyvora.Core.Tasks;
using Windows.UI;

namespace Nyvora.App;

public sealed partial class MainWindow : Window
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x5E, 0xEA, 0xD4));
    private static readonly Brush UpdateBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    private static readonly Brush DueBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));

    private readonly PillStateMachine _machine = new();
    private readonly PillWindowHost _host;
    private readonly DispatcherQueueTimer _awayTimer;
    private readonly DispatcherQueueTimer _reminderTimer;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _sideClock = new();
    private static readonly string TasksPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvora", "tasks.json");

    private readonly JsonTaskStore _store = new(TasksPath);
    private FileSystemWatcher? _watcher;
    private DispatcherQueueTimer? _reloadTimer;
    private bool _reloading;
    private readonly StatusSet _statuses;
    private readonly TagSet _tags;
    private readonly TaskList _tasks;
    private readonly SettingsStore _settings = new();
    private readonly UpdateService _updates;
    private readonly RecurringNudge _statusNudge;
    private readonly DispatcherQueueTimer _updateCheckTimer;

    private SizeD _current = PillAnimator.RestingSize;
    private SizeD _expandedSize = PillAnimator.DefaultExpandedSize;
    private PillAnimator? _animator;
    private PillAnimator? _sideAnimator;
    private bool _rendering;
    private bool _settingsOpen;
    private double _side;      // current width of the settings panel (DIPs)
    private double _sideWidth; // width it has when fully open
    private bool _passiveExpand;
    private bool _focusMode; // a reminder card is showing instead of the list
    private static readonly SizeD FocusSize = new(380, 200);

    private SizeD ExpandedTarget => _focusMode ? FocusSize : _expandedSize;
    private int _frames;
    private bool _focusInputWhenOpen;

    public MainWindow()
    {
        InitializeComponent();
        _updates = new UpdateService(_settings);
        _statusNudge = new RecurringNudge(TimeSpan.FromMinutes(_settings.Current.StatusReminderMinutes), DateTimeOffset.Now);
        _host = new PillWindowHost(this, _settings.Current.AlwaysOnTop);
        _expandedSize = ExpandedSizing.FromSettings(
            _settings.Current.ExpandedWidth, _settings.Current.ExpandedHeight, _host.AvailableSizeDip);
        ApplyPanelSize();
        // handledEventsToo: buttons and text boxes mark PointerPressed handled, but they still need to pull focus.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);

        _statuses = new StatusSet(_settings.Current.Statuses);
        _tags = new TagSet(_settings.Current.Tags);
        _tasks = new TaskList(LoadTasks(), _statuses, _tags);
        _tags.Changed += () =>
        {
            _settings.Current.Tags = _tags.Export();
            _settings.Save();
            _tasks.ApplyTags(); // deleted tags disappear from tasks
        };
        _statuses.Changed += () =>
        {
            _settings.Current.Statuses = _statuses.Export();
            _settings.Save();
            _tasks.ApplyStatuses(); // moves tasks out of removed statuses and re-syncs "done"
        };
        _tasks.Changed += OnTasksChanged;
        _updates.Changed += UpdateBadge;
        _updates.RestartRequested += Close;
        Panel.Attach(_tasks, _updates, _statuses, _tags);
        Panel.SettingsRequested += ToggleSettings;
        Side.Attach(_statuses, _tags, _updates, _settings.Current.AlwaysOnTop, _statusNudge.Enabled, _settings.Current.LaunchAtSignIn);
        Side.LaunchAtSignInChanged += on =>
        {
            StartupService.SetLaunchAtSignIn(on);
            _settings.Current.LaunchAtSignIn = on;
            _settings.Save();
        };
        // Re-applied every launch so the entry follows the exe if the install folder ever moves.
        StartupService.SetLaunchAtSignIn(_settings.Current.LaunchAtSignIn);
        StartupService.EnsureStartMenuShortcut();
        Side.StatusReminderChanged += on =>
        {
            var minutes = on ? 60 : 0;
            _settings.Current.StatusReminderMinutes = minutes;
            _settings.Save();
            _statusNudge.SetInterval(TimeSpan.FromMinutes(minutes), DateTimeOffset.Now);
        };
        Side.AlwaysOnTopChanged += on =>
        {
            _host.SetAlwaysOnTop(on);
            _settings.Current.AlwaysOnTop = on;
            _settings.Save();
        };
        Panel.FocusEnded += () =>
        {
            if (_focusMode)
            {
                _focusMode = false;
                Collapse("reminder handled");
            }
        };
        Panel.FocusOpenAllRequested += () =>
        {
            _focusMode = false;
            ApplyPanelSize();
            Start(_expandedSize, PillAnimator.SideDuration); // grow from the card to the full list
            _host.Focus();
            Panel.FocusInput();
        };
        Panel.CollapseRequested += () => Collapse("collapse button");
        Panel.QuitRequested += Close;

        var queue = DispatcherQueue.GetForCurrentThread();
        WatchTasksFile(queue);
        _awayTimer = queue.CreateTimer();
        _awayTimer.Interval = TimeSpan.FromMilliseconds(50);
        _awayTimer.Tick += (_, _) =>
        {
            // Always sample so the press edge stays accurate, but a click inside an open popup (time picker,
            // menus) lands outside the small window and must not collapse the pill.
            var outside = _host.PollClickOutside();
            if (outside && !AnyPopupOpen())
            {
                Collapse("click outside");
            }
        };

        // Coarse on purpose: reminders only need minute-level accuracy, and polling the clock
        // also survives sleep/resume and manual clock changes.
        _reminderTimer = queue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(20);
        _reminderTimer.Tick += (_, _) => CheckReminders();

        // One-shot, a few seconds after launch so the check never competes with startup.
        _updateCheckTimer = queue.CreateTimer();
        _updateCheckTimer.Interval = TimeSpan.FromSeconds(8);
        _updateCheckTimer.IsRepeating = false;
        _updateCheckTimer.Tick += async (_, _) =>
        {
            try
            {
                await _updates.AutoCheckAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Automatic update check failed", ex);
            }
        };

        _machine.StateChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            _watcher?.Dispose();
            StopFrames();
            _awayTimer.Stop();
            _reminderTimer.Stop();
            _updateCheckTimer.Stop();
            _host.Dispose();
            Log.Info("Closed");
        };
    }

    /// <summary>Another launch of the app asked this copy to open; behaves like clicking the pill.</summary>
    public void ShowFromSecondLaunch()
    {
        if (_machine.State is PillState.Resting or PillState.Collapsing)
        {
            _machine.RequestExpand();
        }
    }

    public void Show()
    {
        _host.Show();
        UpdateContent();
        _reminderTimer.Start();
        CheckReminders();
        UpdateService.CleanupOldUpdates();
        _updateCheckTimer.Start();
        Log.Info("Started");
    }

    private List<TaskItem> LoadTasks()
    {
        try
        {
            var items = _store.Load();
            if (_store.RecoveredFromCorruption)
            {
                Log.Error("tasks.json was unreadable; moved aside as tasks.json.corrupt");
            }

            return items;
        }
        catch (IOException ex)
        {
            Log.Error("Could not read tasks", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error("Could not read tasks", ex);
        }

        return new List<TaskItem>();
    }

    private void OnTasksChanged()
    {
        if (_reloading)
        {
            UpdateBadge(); // these tasks just came from the file; writing them back would only echo
            return;
        }

        try
        {
            using var gate = TaskFileLock.Acquire(TasksPath, TimeSpan.FromSeconds(3));
            _store.Save(_tasks.Items);
        }
        catch (IOException ex)
        {
            Log.Error("Could not save tasks", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error("Could not save tasks", ex);
        }

        UpdateBadge();
    }

    /// <summary>Picks up changes made to tasks.json by another program (the MCP server) while the app is running.</summary>
    private void WatchTasksFile(DispatcherQueue queue)
    {
        try
        {
            var dir = Path.GetDirectoryName(TasksPath)!;
            Directory.CreateDirectory(dir);
            _reloadTimer = queue.CreateTimer();
            _reloadTimer.Interval = TimeSpan.FromMilliseconds(350); // one burst of events becomes one reload
            _reloadTimer.IsRepeating = false;
            _reloadTimer.Tick += (_, _) => ReloadFromDisk();

            _watcher = new FileSystemWatcher(dir, "tasks.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            void Poke(object? sender, EventArgs e) => queue.TryEnqueue(() =>
            {
                _reloadTimer?.Stop();
                _reloadTimer?.Start();
            });
            _watcher.Changed += Poke;
            _watcher.Created += Poke;
            _watcher.Renamed += Poke;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error("Could not watch tasks.json; changes by other programs will show after a restart", ex);
        }
    }

    private void ReloadFromDisk()
    {
        try
        {
            List<TaskItem> loaded;
            using (var gate = TaskFileLock.Acquire(TasksPath, TimeSpan.FromSeconds(3)))
            {
                loaded = _store.Load();
                if (_store.RecoveredFromCorruption)
                {
                    // Never let an unreadable file wipe what we hold: put our copy back.
                    Log.Error("tasks.json was unreadable after an outside change; restored it from memory");
                    _store.Save(_tasks.Items);
                    return;
                }
            }

            if (TaskSnapshot.Same(_tasks.Items, loaded))
            {
                return; // our own save, or no real change
            }

            Log.Info($"tasks.json changed outside the app; reloading ({loaded.Count} tasks)");
            _reloading = true;
            try
            {
                _tasks.ReplaceItems(loaded);
            }
            finally
            {
                _reloading = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not reload tasks", ex);
        }
    }

    private void CheckReminders()
    {
        CheckStatusNudge();
        var fired = _tasks.TakePendingNotifications(DateTimeOffset.Now);
        UpdateBadge();
        if (fired.Count == 0)
        {
            return;
        }

        Log.Info($"{fired.Count} reminder(s) due");
        if (_machine.State is PillState.Resting or PillState.Collapsing)
        {
            // Closed pill: show just the reminder as a card. If the list is already open, leave it alone.
            _focusMode = true;
            Panel.ShowFocus(fired.Select(f => f.Id));
            ApplyPanelSize();
        }

        ExpandPassively();
    }

    /// <summary>Opens the pill without taking focus from whatever the user is doing.</summary>
    private void ExpandPassively()
    {
        if (_machine.State is PillState.Resting or PillState.Collapsing)
        {
            _passiveExpand = true;
            _machine.RequestExpand();
        }
    }

    private void CheckStatusNudge()
    {
        if (!_statusNudge.TryFire(DateTimeOffset.Now, _tasks.OpenCount))
        {
            return;
        }

        Log.Info($"Status reminder ({_tasks.OpenCount} open)");
        Panel.ShowNudge($"Time to update your task statuses ({_tasks.OpenCount} open)");
        ExpandPassively();
    }

    private void UpdateBadge()
    {
        var due = _tasks.DueCount(DateTimeOffset.Now);
        RestingLabel.Text = PillLabel.For(due, _tasks.OpenCount);
        StatusDot.Fill = due > 0 ? DueBrush
            : _updates.State == UpdateState.Available ? UpdateBrush
            : AccentBrush;
    }

    // ----- resizing -----

    private bool _resizing;
    private NativeMethods.POINT _resizeStartCursor;
    private SizeD _resizeStartSize;

    /// <summary>The panel keeps a fixed size (the target) so its layout never reflows while the window animates.</summary>
    private void ApplyPanelSize()
    {
        Panel.Width = ExpandedTarget.Width;
        Panel.Height = ExpandedTarget.Height;
        Side.Width = _sideWidth;
        Side.Height = _expandedSize.Height;
        Side.Margin = new Thickness(_expandedSize.Width, 0, 0, 0);
        SideDivider.Margin = new Thickness(_expandedSize.Width, 14, 0, 14);
    }

    private void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_machine.State != PillState.Expanded || !NativeMethods.GetCursorPos(out _resizeStartCursor))
        {
            return;
        }

        _resizing = true;
        _resizeStartSize = _expandedSize;
        Grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        // Screen coordinates, because the window itself moves while it is resized around the top-center anchor.
        if (!_resizing || !NativeMethods.GetCursorPos(out var p))
        {
            return;
        }

        var scale = _host.Scale;
        SetExpandedSize(ExpandedSizing.Resize(
            _resizeStartSize,
            (p.X - _resizeStartCursor.X) / scale,
            (p.Y - _resizeStartCursor.Y) / scale,
            _host.AvailableSizeDip));
    }

    private void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        Grip.ReleasePointerCapture(e.Pointer);
        EndResize();
    }

    private void OnGripCaptureLost(object sender, PointerRoutedEventArgs e) => EndResize();

    private void OnGripDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_machine.State == PillState.Expanded)
        {
            SetExpandedSize(ExpandedSizing.Clamp(PillAnimator.DefaultExpandedSize, _host.AvailableSizeDip));
            SaveExpandedSize();
        }
    }

    private void SetExpandedSize(SizeD size)
    {
        _expandedSize = size;
        _current = size;
        ApplyPanelSize();
        _host.Apply(_current, _side);
        UpdateContent();
    }

    private void EndResize()
    {
        if (_resizing)
        {
            _resizing = false;
            SaveExpandedSize();
            Log.Info($"Resized to {_expandedSize.Width:F0}x{_expandedSize.Height:F0}");
        }
    }

    private void SaveExpandedSize()
    {
        _settings.Current.ExpandedWidth = _expandedSize.Width;
        _settings.Current.ExpandedHeight = _expandedSize.Height;
        _settings.Save();
    }

    private void OnRestingTapped(object sender, TappedRoutedEventArgs e) => _machine.RequestExpand();

    /// <summary>A click inside an expanded pill is the user engaging with it, so it may take focus (typing).</summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_machine.State is PillState.Expanding or PillState.Expanded)
        {
            _host.Focus();
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = Collapse("Esc");
    }

    private bool AnyPopupOpen() =>
        Root.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    private bool Collapse(string reason)
    {
        var from = _machine.State;
        var accepted = _machine.RequestCollapse();
        if (accepted)
        {
            Log.Info($"Collapse ({reason}) while {from}");
        }

        return accepted;
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(PillState previous, PillState next)
    {
        Log.Info($"State {previous} -> {next}");
        switch (next)
        {
            case PillState.Expanding:
                if (_passiveExpand)
                {
                    _passiveExpand = false;
                }
                else
                {
                    _host.Focus();
                    _focusInputWhenOpen = true; // ready to type the moment it has opened
                }

                _host.ResetClickTracking();
                _awayTimer.Start();
                UpdateBadge();
                // The display may have changed since the size was chosen.
                _expandedSize = ExpandedSizing.Clamp(_expandedSize, _host.AvailableSizeDip);
                ApplyPanelSize();
                Start(ExpandedTarget, PillAnimator.ExpandDuration);
                break;
            case PillState.Collapsing:
                if (_settingsOpen || _side > 0)
                {
                    _settingsOpen = false;
                    StartSide(0); // the settings panel closes together with the pill
                }

                Start(PillAnimator.RestingSize, PillAnimator.CollapseDuration);
                break;
            case PillState.Resting:
                Panel.ClearNudge();
                Panel.ClearFocus();
                _focusMode = false;
                _settingsOpen = false;
                _side = 0;
                _sideAnimator = null;
                _awayTimer.Stop();
                _host.SetNoActivate(true);
                break;
        }
    }

    private void Start(SizeD target, TimeSpan duration)
    {
        // Begin from the on-screen size so reversing mid-animation is continuous.
        _animator = new PillAnimator(_current, target, duration);
        _frames = 0;
        _clock.Restart();
        StartFrames();
    }

    // ----- settings side panel -----

    private void ToggleSettings()
    {
        if (_machine.State != PillState.Expanded)
        {
            return;
        }

        _settingsOpen = !_settingsOpen;
        if (_settingsOpen)
        {
            _sideWidth = ExpandedSizing.SideWidth(_expandedSize, _host.AvailableSizeDip);
            ApplyPanelSize();
        }

        StartSide(_settingsOpen ? _sideWidth : 0);
        Log.Info(_settingsOpen ? "Settings opened" : "Settings closed");
    }

    private void OnSettingsClose() => ToggleSettings();

    private void StartSide(double target)
    {
        _sideAnimator = new PillAnimator(new SizeD(_side, 0), new SizeD(target, 0), PillAnimator.SideDuration);
        _sideClock.Restart();
        StartFrames();
    }

    // Driven by the compositor's frame event rather than a timer: one update per displayed frame, so the window
    // is never resized twice between two frames (which shows up as flicker).
    private void StartFrames()
    {
        if (!_rendering)
        {
            CompositionTarget.Rendering += OnFrame;
            _rendering = true;
        }
    }

    private void StopFrames()
    {
        if (_rendering)
        {
            CompositionTarget.Rendering -= OnFrame;
            _rendering = false;
        }
    }

    private void OnFrame(object? sender, object e)
    {
        if (_animator is null && _sideAnimator is null)
        {
            StopFrames();
            return;
        }

        var pillDone = false;
        if (_animator is { } a)
        {
            var elapsed = _clock.Elapsed;
            _frames++;
            _current = a.Sample(elapsed);
            if (a.IsComplete(elapsed))
            {
                _animator = null;
                pillDone = true;
                Log.Info($"Animation finished: {_frames} frames in {elapsed.TotalMilliseconds:F0} ms, size {_current.Width:F0}x{_current.Height:F0}");
            }
        }

        if (_sideAnimator is { } sa)
        {
            var elapsed = _sideClock.Elapsed;
            _side = sa.Sample(elapsed).Width;
            if (sa.IsComplete(elapsed))
            {
                _sideAnimator = null;
            }
        }

        _host.Apply(_current, _side);
        UpdateContent();

        if (pillDone)
        {
            _machine.CompleteAnimation(_machine.Generation);
            UpdateContent();
            if (_focusInputWhenOpen && _machine.State == PillState.Expanded)
            {
                _focusInputWhenOpen = false;
                Panel.FocusInput();
            }
        }

        if (_animator is null && _sideAnimator is null)
        {
            StopFrames();
        }
    }

    /// <summary>Cross-fades resting/expanded content from the current size; no layout depends on window size.</summary>
    private void UpdateContent()
    {
        var t = PillAnimator.ExpansionProgress(_current, ExpandedTarget);
        RestingContent.Opacity = Math.Clamp(1 - t * 3, 0, 1);
        RestingContent.IsHitTestVisible = _machine.State is PillState.Resting or PillState.Collapsing && t < 0.5;
        Panel.Opacity = Math.Clamp((t - 0.45) / 0.55, 0, 1);
        Panel.IsHitTestVisible = _machine.State is PillState.Expanded;

        var side = _sideWidth > 0 ? Math.Clamp(_side / _sideWidth, 0, 1) : 0;
        Side.Opacity = Math.Clamp((side - 0.3) / 0.7, 0, 1);
        SideDivider.Opacity = Side.Opacity;
        Side.IsHitTestVisible = _settingsOpen && side > 0.9;

        // The grip resizes around the pill's center, which no longer holds while the side panel is out.
        Grip.Opacity = Panel.Opacity * (1 - side);
        Grip.IsHitTestVisible = _machine.State is PillState.Expanded && !_focusMode && !_settingsOpen && side == 0;
    }
}
