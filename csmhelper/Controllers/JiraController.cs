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

        // �������� �������� ����� - ������ ��� ��������������
        public async Task<IActionResult> Index()
        {
            // ��������� �����������
            if (!IsAuthenticated())
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Jira" });
            }

            var isAuthenticated = await _jiraService.IsAuthenticatedAsync();
            if (!isAuthenticated)
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Jira" });
            }

            ViewBag.IsAuthenticated = isAuthenticated;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Epics([FromBody] JiraEpicsRequest request)
        {
            if (!IsAuthenticated())
                return Json(new JiraEpicsResponse { Success = false, Error = "Требуется авторизация" });

            if (request == null || string.IsNullOrWhiteSpace(request.ProjectKey))
                return Json(new JiraEpicsResponse { Success = false, Error = "Не указан ключ проекта" });

            try
            {
                var result = await _jiraService.GetEpicsByProjectAsync(request.ProjectKey);
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке эпиков проекта");
                return Json(new JiraEpicsResponse { Success = false, Error = $"Ошибка: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTasks([FromBody]TaskCreationModel model)
        {
            // ��������� ����������� ��� API ��������
            if (!IsAuthenticated())
            {
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = "��������� �����������"
                });
            }

            if (!ModelState.IsValid)
            {
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = "�������� ������ ��� �������� �����"
                });
            }

            try
            {
                var result = await _jiraService.CreateLinkedTasksAsync(model);

                // �������� ��������� ��� �������
                _logger.LogInformation($"Created {result.TasksCreated} tasks with {result.LinksCreated} links");
                if (result.Tasks != null)
                {
                    foreach (var task in result.Tasks)
                    {
                        _logger.LogInformation($"Created task: {task.Key} - {task.Summary} - {task.Url}");
                    }
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "������ ��� �������� �����");
                return Json(new TaskCreationResponse
                {
                    Success = false,
                    Error = $"������ ��� �������� �����: {ex.Message}"
                });
            }
        }

        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("IsAuthenticated") == "true";
        }
    }

}
