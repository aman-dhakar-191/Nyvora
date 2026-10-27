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
    private static readonly Brush DueBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));

    private readonly PillStateMachine _machine = new();
    private readonly PillWindowHost _host;
    private readonly DispatcherQueueTimer _frameTimer;
    private readonly DispatcherQueueTimer _awayTimer;
    private readonly DispatcherQueueTimer _reminderTimer;
    private readonly Stopwatch _clock = new();
    private readonly JsonTaskStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvora", "tasks.json"));
    private readonly TaskList _tasks;

    private SizeD _current = PillAnimator.RestingSize;
    private PillAnimator? _animator;
    private bool _passiveExpand;

    public MainWindow()
    {
        InitializeComponent();
        _host = new PillWindowHost(this);
        // handledEventsToo: buttons and text boxes mark PointerPressed handled, but they still need to pull focus.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);

        _tasks = new TaskList(LoadTasks());
        _tasks.Changed += OnTasksChanged;
        Panel.Attach(_tasks);
        Panel.CollapseRequested += () => _machine.RequestCollapse();
        Panel.QuitRequested += Close;

        var queue = DispatcherQueue.GetForCurrentThread();
        _frameTimer = queue.CreateTimer();
        _frameTimer.Interval = TimeSpan.FromMilliseconds(8);
        _frameTimer.Tick += OnFrame;

        _awayTimer = queue.CreateTimer();
        _awayTimer.Interval = TimeSpan.FromMilliseconds(50);
        _awayTimer.Tick += (_, _) =>
        {
            if (_host.PollClickOutside())
            {
                _machine.RequestCollapse();
            }
        };

        // Coarse on purpose: reminders only need minute-level accuracy, and polling the clock
        // also survives sleep/resume and manual clock changes.
        _reminderTimer = queue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(20);
        _reminderTimer.Tick += (_, _) => CheckReminders();

        _machine.StateChanged += OnStateChanged;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _frameTimer.Stop();
            _awayTimer.Stop();
            _reminderTimer.Stop();
            _host.Dispose();
            Log.Info("Closed");
        };
    }

    public void Show()
    {
        _host.Show();
        UpdateContent();
        _reminderTimer.Start();
        CheckReminders();
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
        try
        {
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

    private void CheckReminders()
    {
        var fired = _tasks.TakePendingNotifications(DateTimeOffset.Now);
        UpdateBadge();
        if (fired.Count == 0)
        {
            return;
        }

        Log.Info($"{fired.Count} reminder(s) due");

        // Surface the reminder without taking focus from whatever the user is doing.
        if (_machine.State is PillState.Resting or PillState.Collapsing)
        {
            _passiveExpand = true;
            _machine.RequestExpand();
        }
    }

    private void UpdateBadge()
    {
        var due = _tasks.DueCount(DateTimeOffset.Now);
        RestingLabel.Text = due > 0 ? $"{due} due" : "Nyvora";
        StatusDot.Fill = due > 0 ? DueBrush : AccentBrush;
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
        args.Handled = _machine.RequestCollapse();
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            _machine.RequestCollapse(); // fast path; the click poll covers cases where this never fires
        }
    }

    private void OnStateChanged(PillState previous, PillState next)
    {
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
                }

                _host.ResetClickTracking();
                _awayTimer.Start();
                UpdateBadge();
                Start(PillAnimator.ExpandedSize, PillAnimator.ExpandDuration);
                break;
            case PillState.Collapsing:
                Start(PillAnimator.RestingSize, PillAnimator.CollapseDuration);
                break;
            case PillState.Resting:
                _awayTimer.Stop();
                _host.SetNoActivate(true);
                break;
        }
    }

    private void Start(SizeD target, TimeSpan duration)
    {
        // Begin from the on-screen size so reversing mid-animation is continuous.
        _animator = new PillAnimator(_current, target, duration);
        _clock.Restart();
        _frameTimer.Start();
    }

    private void OnFrame(DispatcherQueueTimer sender, object args)
    {
        if (_animator is null)
        {
            _frameTimer.Stop();
            return;
        }

        var elapsed = _clock.Elapsed;
        _current = _animator.Sample(elapsed);
        _host.Apply(_current);
        UpdateContent();

        if (_animator.IsComplete(elapsed))
        {
            _frameTimer.Stop();
            _animator = null;
            _machine.CompleteAnimation(_machine.Generation);
            UpdateContent();
        }
    }

    /// <summary>Cross-fades resting/expanded content from the current size; no layout depends on window size.</summary>
    private void UpdateContent()
    {
        var t = PillAnimator.ExpansionProgress(_current);
        RestingContent.Opacity = Math.Clamp(1 - t * 3, 0, 1);
        RestingContent.IsHitTestVisible = _machine.State is PillState.Resting or PillState.Collapsing && t < 0.5;
        Panel.Opacity = Math.Clamp((t - 0.45) / 0.55, 0, 1);
        Panel.IsHitTestVisible = _machine.State is PillState.Expanded;
    }
}
