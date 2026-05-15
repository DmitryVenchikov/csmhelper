using csmhelper.Models;
using csmhelper.services;
using Microsoft.AspNetCore.Mvc;

namespace csmhelper.Controllers
{
    public class GanttController : Controller
    {
        private readonly IGanttService _ganttService;

        public GanttController(IGanttService ganttService)
        {
            _ganttService = ganttService;
        }

        // GET /Gantt
        public IActionResult Index()
        {
            return View();
        }

        // ── Data ──────────────────────────────────────────────────────

        [HttpGet("/Gantt/Data")]
        public async Task<IActionResult> GetData()
        {
            var data = await _ganttService.GetDataAsync();
            return Json(new { success = true, data });
        }

        // ── Team Members ──────────────────────────────────────────────

        [HttpPost("/Gantt/Members")]
        public async Task<IActionResult> AddMember([FromBody] TeamMember member)
        {
            if (string.IsNullOrWhiteSpace(member.Name))
                return Json(new { success = false, error = "Имя участника обязательно" });

            var created = await _ganttService.AddMemberAsync(member);
            return Json(new { success = true, member = created });
        }

        [HttpPut("/Gantt/Members/{id}")]
        public async Task<IActionResult> UpdateMember(string id, [FromBody] TeamMember member)
        {
            member.Id = id;
            var ok = await _ganttService.UpdateMemberAsync(member);
            return Json(new { success = ok });
        }

        [HttpDelete("/Gantt/Members/{id}")]
        public async Task<IActionResult> DeleteMember(string id)
        {
            var ok = await _ganttService.DeleteMemberAsync(id);
            return Json(new { success = ok });
        }

        // ── Vacations ─────────────────────────────────────────────────

        [HttpPost("/Gantt/Vacations")]
        public async Task<IActionResult> AddVacation([FromBody] VacationPeriod vacation)
        {
            if (string.IsNullOrWhiteSpace(vacation.MemberId))
                return Json(new { success = false, error = "Участник обязателен" });

            if (vacation.EndDate < vacation.StartDate)
                return Json(new { success = false, error = "Дата окончания не может быть раньше даты начала" });

            var created = await _ganttService.AddVacationAsync(vacation);
            return Json(new { success = true, vacation = created });
        }

        [HttpPut("/Gantt/Vacations/{id}")]
        public async Task<IActionResult> UpdateVacation(string id, [FromBody] VacationPeriod vacation)
        {
            vacation.Id = id;
            var ok = await _ganttService.UpdateVacationAsync(vacation);
            return Json(new { success = ok });
        }

        [HttpDelete("/Gantt/Vacations/{id}")]
        public async Task<IActionResult> DeleteVacation(string id)
        {
            var ok = await _ganttService.DeleteVacationAsync(id);
            return Json(new { success = ok });
        }

        // ── Gantt Tasks ───────────────────────────────────────────────

        [HttpPost("/Gantt/Tasks")]
        public async Task<IActionResult> AddTask([FromBody] GanttTask task)
        {
            if (string.IsNullOrWhiteSpace(task.Title))
                return Json(new { success = false, error = "Название задачи обязательно" });

            if (task.EndDate < task.StartDate)
                return Json(new { success = false, error = "Дата окончания не может быть раньше даты начала" });

            var created = await _ganttService.AddTaskAsync(task);
            return Json(new { success = true, task = created });
        }

        [HttpPut("/Gantt/Tasks/{id}")]
        public async Task<IActionResult> UpdateTask(string id, [FromBody] GanttTask task)
        {
            task.Id = id;
            var ok = await _ganttService.UpdateTaskAsync(task);
            return Json(new { success = ok });
        }

        [HttpDelete("/Gantt/Tasks/{id}")]
        public async Task<IActionResult> DeleteTask(string id)
        {
            var ok = await _ganttService.DeleteTaskAsync(id);
            return Json(new { success = ok });
        }

        // ── Smart scheduling ──────────────────────────────────────────

        /// <summary>
        /// Calculates the effective end date for a task by skipping vacation days.
        /// Query: memberId, start (ISO date), workDays
        /// </summary>
        [HttpGet("/Gantt/ComputeEnd")]
        public async Task<IActionResult> ComputeEnd(string memberId, string start, int workDays)
        {
            if (!DateTime.TryParse(start, out var startDate))
                return Json(new { success = false, error = "Неверный формат даты" });

            if (workDays <= 0)
                return Json(new { success = false, error = "Количество рабочих дней должно быть больше 0" });

            var data = await _ganttService.GetDataAsync();
            var result = _ganttService.ComputeEffectiveEnd(memberId, startDate, workDays, data);

            return Json(new
            {
                success = true,
                originalEnd = result.OriginalEnd.ToString("yyyy-MM-dd"),
                effectiveEnd = result.EffectiveEnd.ToString("yyyy-MM-dd"),
                vacationDaysOverlap = result.VacationDaysOverlap,
                overlappingVacations = result.OverlappingVacations
            });
        }
    }
}
