using csmhelper.Models;

namespace csmhelper.services
{
    internal class ResourceCalendar
    {
        public string Name => _employee.Name;
        public GantRole Role => _employee.Role;

        private readonly GantEmployee _employee;
        private readonly WorkDayCalculator _calc;
        private readonly List<(DateTime Start, DateTime End)> _bookedSlots = new();

        public ResourceCalendar(GantEmployee employee)
        {
            _employee = employee;
            _calc = new WorkDayCalculator(employee);
        }

        public bool CanHandle(string taskType) => _employee.CanHandle(taskType);

        public void BookSlot(DateTime start, DateTime end)
        {
            _bookedSlots.Add((start, end));
            _bookedSlots.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        public double TotalBookedHoursFrom(DateTime from) =>
            _bookedSlots.Where(s => s.End > from).Sum(s => (s.End - s.Start).TotalHours);

        /// <summary>
        /// Finds the earliest available slot of given duration, starting no earlier than earliestStart.
        /// </summary>
        public (DateTime Start, DateTime End) FindAvailableSlot(double durationHours, DateTime earliestStart, int lagMinutes)
        {
            var current = EnsureWorkingTime(earliestStart);
            var durationMinutes = durationHours * 60;

            for (int i = 0; i < 200; i++)
            {
                var candidateEnd = _calc.AddWorkMinutes(current, durationMinutes);

                var conflict = _bookedSlots.FirstOrDefault(s => !(candidateEnd <= s.Start || current >= s.End));
                if (conflict == default)
                    return (current, candidateEnd);

                // Move past the conflicting slot + lag
                current = _calc.AddWorkMinutes(conflict.End, lagMinutes);
            }

            // Fallback: schedule after all booked slots
            var latest = _bookedSlots.Any()
                ? _calc.AddWorkMinutes(_bookedSlots.Max(s => s.End), lagMinutes)
                : EnsureWorkingTime(DateTime.Now);

            return (latest, _calc.AddWorkMinutes(latest, durationMinutes));
        }

        private DateTime EnsureWorkingTime(DateTime dt)
        {
            // Delegate to calculator by attempting to add 0 minutes
            // which normalizes to the next working moment
            if (_calc.IsWorkingHours(dt)) return dt;

            var t = TimeOnly.FromDateTime(dt);
            var ws = _employee.WorkStart;
            var we = _employee.WorkEnd;

            if (dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                var d = dt.Date.AddDays(1);
                while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
                return d.Add(ws.ToTimeSpan());
            }
            if (t < ws) return dt.Date.Add(ws.ToTimeSpan());
            if (t >= we)
            {
                var next = dt.Date.AddDays(1);
                while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next = next.AddDays(1);
                return next.Add(ws.ToTimeSpan());
            }
            // Lunch
            return dt.Date.Add(_employee.LunchEnd.ToTimeSpan());
        }
    }

    internal class ScheduleBuilder
    {
        private readonly List<GantEmployee> _employees;
        private readonly GantScheduleSettings _settings;
        private readonly Dictionary<string, ResourceCalendar> _calendars;

        public List<GantScheduledTask> ScheduledTasks { get; private set; } = new();
        public List<GantScheduledTask> CriticalPath { get; private set; } = new();
        public DateTime? CompletionDate { get; private set; }
        public double TotalDurationHours { get; private set; }
        public double TotalDurationDays { get; private set; }
        public double CriticalSp { get; private set; }
        public double CriticalHours { get; private set; }
        public double CriticalDays { get; private set; }
        public DateTime StartDate { get; private set; }

        public ScheduleBuilder(List<GantEmployee> employees, GantScheduleSettings settings)
        {
            _employees = employees;
            _settings = settings;
            _calendars = employees.ToDictionary(e => e.Name, e => new ResourceCalendar(e));
        }

        public List<GantScheduledTask> Build(List<RawJiraTask> tasks)
        {
            StartDate = DateTime.Now;

            // Sort by priority, then by blockers (tasks with no blockers first)
            var ordered = tasks
                .OrderBy(t => t.PriorityWeight)
                .ThenBy(t => t.BlockedBy.Count)
                .ToList();

            var scheduled = new Dictionary<string, GantScheduledTask>();
            var notScheduled = new List<RawJiraTask>();

            foreach (var task in ordered)
            {
                var suitable = _employees
                    .Where(e => e.CanHandle(task.TaskType))
                    .Select(e => e.Name)
                    .ToList();

                if (!suitable.Any())
                {
                    notScheduled.Add(task);
                    continue;
                }

                // Determine earliest start: after all blockers finish + lag
                var earliestStart = StartDate;
                foreach (var blockerKey in task.BlockedBy)
                {
                    if (scheduled.TryGetValue(blockerKey, out var blocker))
                    {
                        var afterBlocker = blocker.ScheduledEnd.AddMinutes(_settings.TaskTransitionLagMinutes);
                        if (afterBlocker > earliestStart) earliestStart = afterBlocker;
                    }
                }

                // Pick least loaded suitable resource
                var resourceName = suitable
                    .OrderBy(n => _calendars[n].TotalBookedHoursFrom(StartDate))
                    .First();

                var calendar = _calendars[resourceName];
                var (slotStart, slotEnd) = calendar.FindAvailableSlot(
                    task.DurationWorkHours, earliestStart, _settings.TaskTransitionLagMinutes);

                calendar.BookSlot(slotStart, slotEnd);

                var st = new GantScheduledTask
                {
                    Key = task.Key,
                    Summary = task.Summary,
                    StoryPoints = task.StoryPoints,
                    DurationWorkHours = task.DurationWorkHours,
                    DurationWorkDays = task.DurationWorkDays,
                    TaskType = task.TaskType,
                    AssignedResource = resourceName,
                    AssignmentReason = $"Наименее загруженный {GetRoleDisplay(_calendars[resourceName].Role)}",
                    ScheduledStart = slotStart,
                    ScheduledEnd = slotEnd,
                    Priority = task.Priority,
                    DueDate = task.DueDate,
                    Link = task.Link,
                    Status = task.Status,
                    BlockedBy = task.BlockedBy,
                    Blocks = task.Blocks,
                };

                scheduled[task.Key] = st;
                ScheduledTasks.Add(st);
            }

            if (ScheduledTasks.Any())
            {
                CompletionDate = ScheduledTasks.Max(t => t.ScheduledEnd);
                TotalDurationHours = (CompletionDate.Value - StartDate).TotalHours;
                TotalDurationDays = (CompletionDate.Value - StartDate).TotalDays;
            }

            ComputeCriticalPath(scheduled);
            return ScheduledTasks;
        }

        private void ComputeCriticalPath(Dictionary<string, GantScheduledTask> scheduled)
        {
            if (!scheduled.Any()) return;

            // Critical path: chain of tasks ending at CompletionDate with highest total duration
            // Simple approach: tasks that have no slack (end time == project completion or
            // are on the longest dependency chain)

            // Build a map of "finish → tasks"
            // Walk backwards from last-ending tasks through blockedBy chains
            var maxEnd = scheduled.Values.Max(t => t.ScheduledEnd);

            // Start from tasks that end latest
            var endingLast = scheduled.Values
                .Where(t => t.ScheduledEnd == maxEnd)
                .ToList();

            var criticalSet = new HashSet<string>();
            var queue = new Queue<GantScheduledTask>(endingLast);

            while (queue.Any())
            {
                var task = queue.Dequeue();
                if (criticalSet.Contains(task.Key)) continue;
                criticalSet.Add(task.Key);

                foreach (var blockerKey in task.BlockedBy)
                {
                    if (scheduled.TryGetValue(blockerKey, out var blocker))
                        queue.Enqueue(blocker);
                }
            }

            // If critical set is tiny (just 1-2 tasks), widen it:
            // also include any task whose delay would push the completion date
            if (criticalSet.Count <= 2 && scheduled.Count > 2)
            {
                // Include tasks that are on the resource's longest chain
                var byResource = scheduled.Values.GroupBy(t => t.AssignedResource);
                foreach (var group in byResource)
                {
                    var sorted = group.OrderBy(t => t.ScheduledStart).ToList();
                    if (sorted.Any())
                        criticalSet.Add(sorted.Last().Key);
                }
            }

            CriticalPath = scheduled.Values
                .Where(t => criticalSet.Contains(t.Key))
                .OrderBy(t => t.ScheduledStart)
                .ToList();

            foreach (var t in CriticalPath)
                t.IsCritical = true;

            CriticalSp = CriticalPath.Sum(t => t.StoryPoints);
            CriticalHours = CriticalPath.Sum(t => t.DurationWorkHours);
            CriticalDays = CriticalHours / 8.0;
        }

        public Dictionary<string, double> GetUtilization() =>
            ScheduledTasks
                .Where(t => t.AssignedResource != null)
                .GroupBy(t => t.AssignedResource!)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationWorkHours));

        private static string GetRoleDisplay(GantRole role) => role switch
        {
            GantRole.Tester => "Тестировщик",
            GantRole.Analyst => "Аналитик",
            GantRole.BackendDev => "Backend разработчик",
            GantRole.FrontendDev => "Frontend разработчик",
            _ => role.ToString()
        };
    }
}
