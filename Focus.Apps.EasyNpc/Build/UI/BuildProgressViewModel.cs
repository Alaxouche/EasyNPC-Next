using Focus.Apps.EasyNpc.Build.Pipeline;
using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Focus.Apps.EasyNpc.Build.UI
{
    [AddINotifyPropertyChangedInterface]
    public class BuildProgressViewModel<TResult> : IDisposable
    {
        public delegate BuildProgressViewModel<TResult> Factory(IBuildProgress<TResult> model);

        public int CompletedTaskCount { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        [DependsOn(nameof(Elapsed))]
        public string ElapsedText => FormatDuration(Elapsed);
        public string EstimatedTimeRemainingText { get; private set; } = "Estimating...";
        [DependsOn(nameof(RemainingTaskNames))]
        public bool HasRemainingTasks => RemainingTaskNames.Count > 0;
        public bool IsBuildComplete { get; private set; }
        public double OverallProgress { get; private set; }
        [DependsOn(nameof(OverallProgress))]
        public int OverallProgressPercent => (int)Math.Round(OverallProgress * 100);
        public Task<TResult> Outcome => model.Outcome;
        public ObservableCollection<string> RemainingTaskNames { get; private init; } = new();
        public ObservableCollection<BuildTaskViewModel> Tasks { get; private init; } = new();
        public int TotalTaskCount { get; private init; }

        private readonly Subject<bool> disposed = new();
        private readonly IBuildProgress<TResult> model;
        private readonly DateTime startTime = DateTime.UtcNow;
        private readonly DispatcherTimer timer;
        private readonly double totalWeight;
        private double? smoothedTotalSeconds;

        public BuildProgressViewModel(BuildTaskViewModel.Factory taskViewModelFactory, IBuildProgress<TResult> model)
        {
            this.model = model;
            RemainingTaskNames = new(model.AllTaskNames);
            TotalTaskCount = model.AllTaskNames.Count;

            model.Tasks
                .TakeUntil(disposed)
                .ObserveOn(Application.Current.Dispatcher)
                .Subscribe(t =>
                {
                    Tasks.Add(taskViewModelFactory(t));
                    RemainingTaskNames.Remove(t.Name);
                });

            totalWeight = model.AllTaskNames.Sum(TaskWeight);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => UpdateMetrics();
            timer.Start();
            UpdateMetrics();
        }

        // Relative task cost, so the progress bar and ETA track reality. Calibrated from real timings: texture copying
        // dominates (~80%), facegen next (~16%), the rest minor.
        private static double TaskWeight(string name) => name switch
        {
            "Copy Textures" => 45,
            "Copy FaceGen Data" => 9,
            "Pack BSA Archive" => 6,
            "Copy Shared Resources" => 4,
            "Extract Texture Paths" => 2,
            "Apply Face Customizations" => 1,
            "Import NPC Defaults" => 1,
            "Save Patch" => 1,
            _ => 0.3,
        };

        private static string FormatDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero)
                t = TimeSpan.Zero;
            return t.TotalHours >= 1 ?
                $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" :
                $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        // Runs once a second. Progress is weighted by task cost; the ETA smooths the estimated total and subtracts
        // elapsed time so it counts down steadily.
        private void UpdateMetrics()
        {
            Elapsed = DateTime.UtcNow - startTime;
            var completedCount = 0;
            var doneWeight = 0.0;
            foreach (var t in Tasks)
            {
                var weight = TaskWeight(t.Name);
                if (t.State is BuildTaskState.Completed or BuildTaskState.Cancelled or BuildTaskState.Failed)
                {
                    completedCount++;
                    doneWeight += weight;
                }
                else if (t.State == BuildTaskState.Running && t.MaxProgress > 0)
                {
                    doneWeight += weight * Math.Min(1.0, (double)t.CurrentProgress / t.MaxProgress);
                }
            }

            if (model.Outcome.IsCompleted)
            {
                CompletedTaskCount = TotalTaskCount;
                OverallProgress = 1;
                EstimatedTimeRemainingText = "Finished";
                IsBuildComplete = true;
                timer.Stop();
                return;
            }

            CompletedTaskCount = completedCount;
            var progress = totalWeight > 0 ? Math.Min(1.0, doneWeight / totalWeight) : 0;
            OverallProgress = progress;

            if (progress > 0.03 && Elapsed.TotalSeconds > 4)
            {
                var totalEstimateSec = Elapsed.TotalSeconds / progress;
                smoothedTotalSeconds = smoothedTotalSeconds is null ?
                    totalEstimateSec : smoothedTotalSeconds.Value * 0.8 + totalEstimateSec * 0.2;
                var remaining = Math.Max(0, smoothedTotalSeconds.Value - Elapsed.TotalSeconds);
                EstimatedTimeRemainingText = FormatDuration(TimeSpan.FromSeconds(remaining));
            }
            else
            {
                EstimatedTimeRemainingText = "Estimating...";
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed.IsDisposed)
                return;
            timer.Stop();
            disposed.OnNext(true);
            disposed.Dispose();
        }
    }
}
