using csmhelper.Models;
using System.Text.Json;

namespace csmhelper.services
{
    public class GanttService : IGanttService
    {
        private readonly string _dataFilePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public GanttService(IWebHostEnvironment env)
        {
            var dataDir = Path.Combine(env.ContentRootPath, "data");
            Directory.CreateDirectory(dataDir);
            _dataFilePath = Path.Combine(dataDir, "gantt.json");
        }

        public async Task<GanttData> GetDataAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_dataFilePath))
                    return new GanttData();

                var json = await File.ReadAllTextAsync(_dataFilePath);
                return JsonSerializer.Deserialize<GanttData>(json, _jsonOptions) ?? new GanttData();
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task SaveAsync(GanttData data)
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(_dataFilePath, json);
        }

        // ── Team Members ─────────────────────────────────────────────

        public async Task<TeamMember> AddMemberAsync(TeamMember member)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                member.Id = Guid.NewGuid().ToString();
                data.Members.Add(member);
                await SaveAsync(data);
                return member;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> UpdateMemberAsync(TeamMember member)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var idx = data.Members.FindIndex(m => m.Id == member.Id);
                if (idx < 0) return false;
                data.Members[idx] = member;
                await SaveAsync(data);
                return true;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteMemberAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var removed = data.Members.RemoveAll(m => m.Id == id) > 0;
                if (removed)
                {
                    // Cascade delete vacations and tasks belonging to this member
                    data.Vacations.RemoveAll(v => v.MemberId == id);
                    data.Tasks.RemoveAll(t => t.MemberId == id);
                    await SaveAsync(data);
                }
                return removed;
            }
            finally { _lock.Release(); }
        }

        // ── Vacations ─────────────────────────────────────────────────

        public async Task<VacationPeriod> AddVacationAsync(VacationPeriod vacation)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                vacation.Id = Guid.NewGuid().ToString();
                data.Vacations.Add(vacation);
                await SaveAsync(data);
                return vacation;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> UpdateVacationAsync(VacationPeriod vacation)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var idx = data.Vacations.FindIndex(v => v.Id == vacation.Id);
                if (idx < 0) return false;
                data.Vacations[idx] = vacation;
                await SaveAsync(data);
                return true;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteVacationAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var removed = data.Vacations.RemoveAll(v => v.Id == id) > 0;
                if (removed) await SaveAsync(data);
                return removed;
            }
            finally { _lock.Release(); }
        }

        // ── Tasks ─────────────────────────────────────────────────────

        public async Task<GanttTask> AddTaskAsync(GanttTask task)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                task.Id = Guid.NewGuid().ToString();
                data.Tasks.Add(task);
                await SaveAsync(data);
                return task;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> UpdateTaskAsync(GanttTask task)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var idx = data.Tasks.FindIndex(t => t.Id == task.Id);
                if (idx < 0) return false;
                data.Tasks[idx] = task;
                await SaveAsync(data);
                return true;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteTaskAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                var data = await LoadUnsafeAsync();
                var removed = data.Tasks.RemoveAll(t => t.Id == id) > 0;
                if (removed) await SaveAsync(data);
                return removed;
            }
            finally { _lock.Release(); }
        }

        // ── Business Logic ────────────────────────────────────────────

        /// <summary>
        /// Given a start date and a number of working days, compute the effective end date
        /// by skipping weekend days AND days that fall within the member's vacation periods.
        /// </summary>
        public EffectiveDateResult ComputeEffectiveEnd(string memberId, DateTime start, int workDays, GanttData data)
        {
            var memberVacations = data.Vacations
                .Where(v => v.MemberId == memberId)
                .ToList();

            var current = start.Date;
            var daysAdded = 0;
            var vacationDaysSkipped = 0;
            var overlapping = new HashSet<string>();

            while (daysAdded < workDays)
            {
                current = current.AddDays(1);

                if (current.DayOfWeek == DayOfWeek.Saturday ||
                    current.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                var hitVacation = memberVacations.FirstOrDefault(
                    v => current >= v.StartDate.Date && current <= v.EndDate.Date);

                if (hitVacation != null)
                {
                    vacationDaysSkipped++;
                    overlapping.Add(hitVacation.Id);
                    continue;
                }

                daysAdded++;
            }

            var originalEnd = start.Date;
            var rawDays = 0;
            var tmp = start.Date;
            while (rawDays < workDays)
            {
                tmp = tmp.AddDays(1);
                if (tmp.DayOfWeek != DayOfWeek.Saturday && tmp.DayOfWeek != DayOfWeek.Sunday)
                    rawDays++;
            }

            return new EffectiveDateResult
            {
                OriginalEnd = tmp,
                EffectiveEnd = current,
                VacationDaysOverlap = vacationDaysSkipped,
                OverlappingVacations = memberVacations
                    .Where(v => overlapping.Contains(v.Id))
                    .ToList()
            };
        }

        // ── Private helpers ───────────────────────────────────────────

        private async Task<GanttData> LoadUnsafeAsync()
        {
            if (!File.Exists(_dataFilePath))
                return new GanttData();

            var json = await File.ReadAllTextAsync(_dataFilePath);
            return JsonSerializer.Deserialize<GanttData>(json, _jsonOptions) ?? new GanttData();
        }
    }
}
