using Microsoft.UI.Xaml;
using Nikkiward.Services;
using System.Text.Json;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly PlannerStore _plannerStore = new(ApplicationDataPaths.Root);
    private DispatcherTimer? _plannerTimer;
    private PlannerSession? _plannerSession;
    internal PlannerData Planner => _plannerSession?.Current ?? PlannerData.Empty;
    internal bool PlannerRequested { get; set; }
    internal event EventHandler? HomeDataChanged;
    internal DateOnly? SnapshotDay => _journalSnapshot == null ? null : DateOnly.FromDateTime(_journalSnapshot.CapturedAtUtc.LocalDateTime);

    private void OnPlannerRequested(object? sender, EventArgs args)
    {
        PlannerRequested = true;
        ShowLauncher();
    }

    private void StartPlanner()
    {
        try { _plannerSession ??= new(_plannerStore.Load(), _plannerStore.Save); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ViewModel.ReportUiError("无法读取清单与提醒：" + ex.Message);
            return;
        }
        if (_plannerTimer == null)
        {
            _plannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _plannerTimer.Tick += (_, _) => RefreshPlanner();
        }
        _plannerTimer.Start();
        RefreshPlanner(startup: true);
        HomeDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPlanner(bool startup = false)
    {
        try
        {
            if (_plannerSession!.Refresh(DateTimeOffset.Now, ViewModel.GeneralSettings.ChecklistReset, due =>
            {
                var message = startup ? $"错过 {due.Length} 条提醒：" : "";
                App.MainWindow.NotifyReminder(message + string.Join("、", due.Select(r => r.Text)));
            }))
                HomeDataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ViewModel.ReportUiError("清单与提醒：" + ex.Message);
        }
    }

    private void SavePlanner(PlannerData data)
    {
        _plannerSession!.Save(data);
        HomeDataChanged?.Invoke(this, EventArgs.Empty);
    }

    internal async Task PerformPlannerAction(PlannerCommand command)
    {
        if (_plannerTimer?.IsEnabled != true) throw new InvalidOperationException("清单与提醒尚未就绪。");
        if (command.Action == "link-open")
        {
            var link = Planner.Links.FirstOrDefault(l => l.Id == command.Id);
            if (link != null && PlannerData.IsHttps(link.Url) && !await Windows.System.Launcher.LaunchUriAsync(new Uri(link.Url)))
                throw new InvalidOperationException("无法打开系统浏览器。");
            return;
        }
        var now = DateTimeOffset.Now;
        SavePlanner(Planner.Reset(now, ViewModel.GeneralSettings.ChecklistReset).Change(command, now));
    }
}
