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

        // Страница создания задач - БЕЗ ПРОВЕРКИ АВТОРИЗАЦИИ на сервере
        public IActionResult Index()
        {
            // Убираем все проверки авторизации - теперь это делается на фронтенде
            return View();
        }
         
    }
}