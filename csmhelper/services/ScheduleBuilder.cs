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
            // Убеждаемся, что earliestStart в рабочее время
            var current = EnsureWorkingTime(earliestStart);
            var durationMinutes = durationHours * 60;

            for (int i = 0; i < 200; i++)
            {
                var candidateEnd = _calc.AddWorkMinutes(current, durationMinutes);

                // Проверяем пересечения с забронированными слотами
                var conflict = _bookedSlots.FirstOrDefault(s =>
                    !(candidateEnd <= s.Start || current >= s.End));

                if (conflict == default)
                    return (current, candidateEnd);

                // Перемещаемся после конфликтного слота + лаг
                current = _calc.AddWorkMinutes(conflict.End, lagMinutes);
                current = EnsureWorkingTime(current);
            }

            // Fallback
            var latest = _bookedSlots.Any()
                ? _calc.AddWorkMinutes(_bookedSlots.Max(s => s.End), lagMinutes)
                : EnsureWorkingTime(DateTime.Now);
            latest = EnsureWorkingTime(latest);
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

            // Если день нерабочий (выходной или отпуск) — переносим на начало следующего рабочего дня
            if (!_calc.IsWorkday(dt))
            {
                var d = dt.Date.AddDays(1);
                while (!_calc.IsWorkday(d)) d = d.AddDays(1);
                return d.Add(ws.ToTimeSpan());
            }
            if (t < ws) return dt.Date.Add(ws.ToTimeSpan());
            if (t >= we)
            {
                var next = dt.Date.AddDays(1);
                while (!_calc.IsWorkday(next)) next = next.AddDays(1);
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

            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("=== НАЧАЛО ПОСТРОЕНИЯ РАСПИСАНИЯ ===");
            Console.WriteLine(new string('=', 80));

            // Выводим информацию о зависимостях
            Console.WriteLine("\n=== Исходные задачи ===");
            foreach (var task in tasks)
            {
                Console.WriteLine($"{task.Key}:");
                Console.WriteLine($"  SP={task.StoryPoints}, тип={task.TaskType}");
                Console.WriteLine($"  Блокирует: [{string.Join(",", task.Blocks)}]");
                Console.WriteLine($"  Заблокирована: [{string.Join(",", task.BlockedBy)}]");
                Console.WriteLine($"  Длительность: {task.DurationWorkHours:F1} часов ({task.DurationWorkDays:F1} дней)");
            }

            // Сортируем задачи: сначала те, у которых нет блокировок
            var ordered = tasks
                .OrderBy(t => t.BlockedBy.Count)  // Сначала без блокировок
                .ThenBy(t => t.PriorityWeight)
                .ToList();

            var scheduled = new Dictionary<string, GantScheduledTask>();
            var notScheduled = new List<RawJiraTask>();

            // Несколько проходов для учета зависимостей
            var remainingTasks = new List<RawJiraTask>(ordered);
            var maxPasses = ordered.Count * 2;
            var pass = 0;

            while (remainingTasks.Any() && pass < maxPasses)
            {
                pass++;
                var progressMade = false;
                var tasksThisPass = remainingTasks.ToList();

                Console.WriteLine($"\n--- Проход {pass}, осталось задач: {remainingTasks.Count} ---");

                foreach (var task in tasksThisPass)
                {
                    // Проверяем, все ли блокирующие задачи уже запланированы
                    var allBlockersScheduled = true;
                    var latestBlockerEnd = StartDate;

                    Console.WriteLine($"\n  Проверка {task.Key}:");
                    Console.WriteLine($"    Заблокирована задачами: [{string.Join(",", task.BlockedBy)}]");

                    if (!task.BlockedBy.Any())
                    {
                        Console.WriteLine($"    Нет блокировок, можно планировать");
                    }

                    foreach (var blockerKey in task.BlockedBy)
                    {
                        if (scheduled.TryGetValue(blockerKey, out var blocker))
                        {
                            var afterBlocker = blocker.ScheduledEnd.AddMinutes(_settings.TaskTransitionLagMinutes);
                            if (afterBlocker > latestBlockerEnd)
                                latestBlockerEnd = afterBlocker;
                            Console.WriteLine($"    - Блокер {blockerKey} запланирован, закончит {blocker.ScheduledEnd:HH:mm}, после лага {afterBlocker:HH:mm}");
                        }
                        else
                        {
                            Console.WriteLine($"    - Блокер {blockerKey} еще НЕ запланирован");
                            allBlockersScheduled = false;
                            break;
                        }
                    }

                    if (!allBlockersScheduled)
                    {
                        Console.WriteLine($"    -> {task.Key} пропущен (есть незапланированные блокеры)");
                        continue;
                    }

                    // Находим подходящих исполнителей
                    var suitable = _employees
                        .Where(e => e.CanHandle(task.TaskType))
                        .Select(e => e.Name)
                        .ToList();

                    if (!suitable.Any())
                    {
                        Console.WriteLine($"    -> {task.Key} пропущен (нет подходящих исполнителей для {task.TaskType})");
                        notScheduled.Add(task);
                        remainingTasks.Remove(task);
                        continue;
                    }

                    // Выбираем наименее загруженного исполнителя
                    var resourceName = suitable
                        .OrderBy(n => _calendars[n].TotalBookedHoursFrom(StartDate))
                        .First();

                    var calendar = _calendars[resourceName];

                    Console.WriteLine($"    earliestStart = {latestBlockerEnd:HH:mm}");
                    Console.WriteLine($"    resource = {resourceName}");
                    Console.WriteLine($"    duration = {task.DurationWorkHours:F1} часов");

                    // Находим слот с учетом блокировок
                    var (slotStart, slotEnd) = calendar.FindAvailableSlot(
                        task.DurationWorkHours,
                        latestBlockerEnd,
                        _settings.TaskTransitionLagMinutes);

                    Console.WriteLine($"    -> запланирован на {slotStart:dd.MM HH:mm} - {slotEnd:dd.MM HH:mm}");

                    calendar.BookSlot(slotStart, slotEnd);

                    var st = new GantScheduledTask
                    {
                        Key = task.Key,
                        Summary = task.Summary,
                        StoryPoints = task.StoryPoints,
                        DurationWorkHours = task.DurationWorkHours,
                        DurationWorkDays = task.DurationWorkDays,
                        DurationTotalHours = task.DurationTotalHours,
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
                    remainingTasks.Remove(task);
                    progressMade = true;
                }

                if (!progressMade)
                {
                    Console.WriteLine("\n!!! Нет прогресса на проходе, выходим !!!");
                    break;
                }
            }

            // Добавляем незапланированные задачи (если есть)
            foreach (var task in remainingTasks)
            {
                notScheduled.Add(task);
            }

            if (ScheduledTasks.Any())
            {
                CompletionDate = ScheduledTasks.Max(t => t.ScheduledEnd);
                TotalDurationHours = (CompletionDate.Value - StartDate).TotalHours;
                TotalDurationDays = (CompletionDate.Value - StartDate).TotalDays;
            }

            Console.WriteLine("\n=== ИТОГОВОЕ РАСПИСАНИЕ ===");
            foreach (var task in ScheduledTasks.OrderBy(t => t.ScheduledStart))
            {
                Console.WriteLine($"{task.Key}: {task.ScheduledStart:dd.MM HH:mm} - {task.ScheduledEnd:dd.MM HH:mm} (длит: {task.DurationWorkHours:F1}ч)");
            }
            Console.WriteLine(new string('=', 80));

            ComputeCriticalPath(scheduled);
            return ScheduledTasks;
        }
        private void ComputeCriticalPath(Dictionary<string, GantScheduledTask> scheduled)
        {
            if (!scheduled.Any()) return;

            // Build dependency graph
            var graph = new Dictionary<string, List<string>>();      // task -> tasks it blocks
            var reverseGraph = new Dictionary<string, List<string>>(); // task -> tasks it depends on

            foreach (var task in scheduled.Values)
            {
                graph[task.Key] = new List<string>();
                reverseGraph[task.Key] = new List<string>();
            }

            foreach (var task in scheduled.Values)
            {
                foreach (var blocksKey in task.Blocks)
                {
                    if (graph.ContainsKey(blocksKey))
                    {
                        graph[task.Key].Add(blocksKey);
                        reverseGraph[blocksKey].Add(task.Key);
                    }
                }
                foreach (var blockedByKey in task.BlockedBy)
                {
                    if (graph.ContainsKey(blockedByKey))
                    {
                        graph[blockedByKey].Add(task.Key);
                        reverseGraph[task.Key].Add(blockedByKey);
                    }
                }
            }

            // Find end tasks (tasks that don't block anyone)
            var endTasksKeys = new HashSet<string>();
            foreach (var task in scheduled.Values)
            {
                if (!graph[task.Key].Any())
                    endTasksKeys.Add(task.Key);
            }

            // If no end tasks, take all tasks
            if (!endTasksKeys.Any())
                endTasksKeys = new HashSet<string>(scheduled.Keys);

            // Critical path algorithm: start from end tasks and go backwards
            var criticalKeys = new HashSet<string>(endTasksKeys);
            var changed = true;

            while (changed)
            {
                changed = false;
                foreach (var task in scheduled.Values)
                {
                    if (criticalKeys.Contains(task.Key))
                        continue;

                    // Check if any task that depends on this task is critical
                    if (graph[task.Key].Any(dep => criticalKeys.Contains(dep)))
                    {
                        criticalKeys.Add(task.Key);
                        changed = true;
                    }
                }
            }

            // Also add tasks that are on the path from start to critical tasks
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var task in scheduled.Values)
                {
                    if (criticalKeys.Contains(task.Key))
                        continue;

                    // Check if any task that this task depends on is critical
                    if (reverseGraph[task.Key].Any(pred => criticalKeys.Contains(pred)))
                    {
                        criticalKeys.Add(task.Key);
                        changed = true;
                    }
                }
            }

            // Build critical path in order
            var criticalTasks = scheduled.Values
                .Where(t => criticalKeys.Contains(t.Key))
                .ToList();

            // Try to order them by dependencies
            var orderedPath = new List<GantScheduledTask>();
            var visited = new HashSet<string>();
            var queue = new Queue<GantScheduledTask>();

            // Start with tasks that have no dependencies
            var startTasks = criticalTasks.Where(t => !reverseGraph[t.Key].Any(pred => criticalKeys.Contains(pred))).ToList();
            foreach (var task in startTasks)
                queue.Enqueue(task);

            while (queue.Any())
            {
                var task = queue.Dequeue();
                if (visited.Contains(task.Key)) continue;
                visited.Add(task.Key);
                orderedPath.Add(task);

                // Add tasks that depend on current task
                var nextTasks = criticalTasks
                    .Where(t => reverseGraph[t.Key].Contains(task.Key) && !visited.Contains(t.Key))
                    .OrderBy(t => t.ScheduledStart)
                    .ToList();
                foreach (var next in nextTasks)
                    queue.Enqueue(next);
            }

            // If we couldn't order all, just sort by start time
            if (orderedPath.Count != criticalTasks.Count)
            {
                orderedPath = criticalTasks.OrderBy(t => t.ScheduledStart).ToList();
            }

            CriticalPath = orderedPath;

            foreach (var t in CriticalPath)
                t.IsCritical = true;

            // Calculate statistics
            CriticalSp = CriticalPath.Sum(t => t.StoryPoints);
            CriticalHours = CriticalPath.Sum(t => t.DurationWorkHours);

            // Add transition lags between critical tasks
            var lagHours = _settings.TaskTransitionLagMinutes / 60.0;
            for (int i = 0; i < CriticalPath.Count - 1; i++)
            {
                CriticalHours += lagHours;
            }

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
            GantRole.FrontendAM => "Frontend AM",
            GantRole.FrontendAO => "Frontend AO",
            _ => role.ToString()
        };
    }
}