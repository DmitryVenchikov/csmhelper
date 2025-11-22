using csmhelper.Models;
using csmhelper.services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace csmhelper.Controllers
{
    public class JiraController : Controller
    {
        private readonly IJiraService _jiraService;
        private readonly ILogger<JiraController> _logger;

        public JiraController(IJiraService jiraService, ILogger<JiraController> logger)
        {
            _jiraService = jiraService;
            _logger = logger;
        }

        // Страница создания задач - только для авторизованных
        public async Task<IActionResult> Index()
        {
            // Проверяем авторизацию
            if (!IsAuthenticated())
            {
                return RedirectToAction("Login", "Auth");
            }

            var isAuthenticated = await _jiraService.IsAuthenticatedAsync();
            if (!isAuthenticated)
            {
                return RedirectToAction("Login", "Auth");
            }

            ViewBag.IsAuthenticated = isAuthenticated;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateTasks(TaskCreationModel model)
        {
            // Проверяем авторизацию для API запросов
            if (!IsAuthenticated())
            {
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = "Требуется авторизация"
                });
            }

            if (!ModelState.IsValid)
            {
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = "Неверные данные для создания задач"
                });
            }

            try
            {
                var result = await _jiraService.CreateLinkedTasksAsync(model);
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании задач");
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = $"Ошибка при создании задач: {ex.Message}"
                });
            }
        }

        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("IsAuthenticated") == "true";
        }
    }

}
