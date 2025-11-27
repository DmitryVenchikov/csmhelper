using csmhelper.Models;
using csmhelper.services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace csmhelper.Controllers
{
    public class AuthController : Controller
    {
        private readonly IJiraService _jiraService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IJiraService jiraService, ILogger<AuthController> logger)
        {
            _jiraService = jiraService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            // Убираем все проверки - просто показываем страницу логина
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public IActionResult Logout()
        {
            // Очищаем сессию если что-то было
            HttpContext.Session.Clear();
            return Json(new AuthResponse
            {
                Success = true,
                Message = "Сессия очищена"
            });
        }

        [HttpGet]
        public IActionResult AuthStatus()
        {
            // Всегда возвращаем false - авторизация теперь на фронтенде
            return Json(new AuthResponse
            {
                Success = true,
                Authenticated = false // Frontend will handle its own auth
            });
        }
    }
}