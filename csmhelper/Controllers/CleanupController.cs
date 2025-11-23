using csmhelper.Models;
using csmhelper.services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Newtonsoft.Json;
namespace csmhelper.Controllers
{
    public class CleanupController: Controller
    {
        private readonly IJiraService _jiraService;
        private readonly ILogger<CleanupController> _logger;

        public CleanupController(IJiraService jiraService, ILogger<CleanupController> logger)
        {
            _jiraService = jiraService;
            _logger = logger;
        }

        // Страница очистки задач - только для авторизованных
        public async Task<IActionResult> Index()
        {
            // Проверяем авторизацию
            if (!IsAuthenticated())
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Cleanup" });
            }

            var isAuthenticated = await _jiraService.IsAuthenticatedAsync();
            if (!isAuthenticated)
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Cleanup" });
            }

            ViewBag.IsAuthenticated = isAuthenticated;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SearchTasks([FromBody] CleanupFilterModel filters)
        {
            if (!IsAuthenticated())
            {
                return Json(new { success = false, error = "Требуется авторизация" });
            }

            try
            {
                var tasks = await _jiraService.SearchTasksAsync(filters);
                return Json(new { success = true, tasks = tasks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске задач");
                return Json(new { success = false, error = $"Ошибка при поиске задач: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProcessTasks([FromBody] ProcessTasksModel model)
        {
            if (!IsAuthenticated())
            {
                return Json(new { success = false, error = "Требуется авторизация" });
            }

            try
            {
                Console.WriteLine($"ProcessTasks called: {model.TaskKeys.Count} tasks, Action: {model.ActionType}, TargetStatus: {model.TargetStatus}, Resolution: {model.Resolution}");

                var results = await _jiraService.ProcessTasksAsync(model.TaskKeys, model.ActionType, model.TargetStatus, model.Resolution);

                Console.WriteLine($"Process tasks completed: {results.Count} results");
                foreach (var result in results)
                {
                    Console.WriteLine($"Result: {result.Key} - Success: {result.Success} - Message: {result.Message}");
                }

                return Json(new { success = true, results = results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке задач");
                return Json(new { success = false, error = $"Ошибка при обработке задач: {ex.Message}" });
            }
        }

        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("IsAuthenticated") == "true";
        }
    }

    public class CleanupFilterModel
    {
        public List<string> EpicKeys { get; set; } = new List<string>();
        public List<string> ExcludedEpics { get; set; } = new List<string>();
        public List<string> Projects { get; set; } = new List<string>();
        public List<string> ExcludedProjects { get; set; } = new List<string>();
        public List<string> Statuses { get; set; } = new List<string>();
        public List<string> ExcludedStatuses { get; set; } = new List<string>();
        public DateTime? CleanupCreatedDate { get; set; }
        public DateTime? ExcludedCreatedDate { get; set; }
        public DateTime? CleanupUpdatedDate { get; set; }
        public DateTime? ExcludedUpdatedDate { get; set; }
        public string SprintFilter { get; set; }
        public List<string> ExcludedSprints { get; set; } = new List<string>();
    }

    public class ProcessTasksModel
    {
        public List<string> TaskKeys { get; set; } = new List<string>();
        public string ActionType { get; set; } // "delete" или "transition"
        public string TargetStatus { get; set; }
        public string Resolution { get; set; }
    }


}
