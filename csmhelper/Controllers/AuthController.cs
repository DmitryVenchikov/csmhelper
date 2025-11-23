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
            // Логируем полученный returnUrl для отладки
            Console.WriteLine($"Login page requested with returnUrl: {returnUrl}");

            // Если returnUrl не указан, пробуем получить из sessionStorage через JavaScript
            if (string.IsNullOrEmpty(returnUrl))
            {
                returnUrl = "/Jira"; // значение по умолчанию
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginRequestModel model)
        {
            Console.WriteLine($"Login attempt - ReturnUrl: {model.ReturnUrl}");

            if (model == null)
            {
                return Json(new AuthResponse
                {
                    Success = false,
                    Error = "Данные не получены"
                });
            }

            if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
            {
                return Json(new AuthResponse
                {
                    Success = false,
                    Error = "Логин и пароль обязательны"
                });
            }

            try
            {
                var success = await _jiraService.AuthenticateAsync(model.Username, model.Password);

                if (success)
                {
                    HttpContext.Session.SetString("IsAuthenticated", "true");

                    // Используем returnUrl из запроса или значение по умолчанию
                    var returnUrl = model.ReturnUrl ?? "/Jira";
                    Console.WriteLine($"Authentication successful, redirecting to: {returnUrl}");

                    return Json(new AuthResponse
                    {
                        Success = true,
                        Authenticated = true,
                        Message = "Успешная аутентификация",
                        ReturnUrl = returnUrl
                    });
                }
                else
                {
                    return Json(new AuthResponse
                    {
                        Success = false,
                        Authenticated = false,
                        Error = "Ошибка аутентификации. Проверьте логин и пароль."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при аутентификации");
                return Json(new AuthResponse
                {
                    Success = false,
                    Authenticated = false,
                    Error = $"Ошибка аутентификации: {ex.Message}"
                });
            }
        }

        public class LoginRequestModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string ReturnUrl { get; set; }
        }
    
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _jiraService.LogoutAsync();
            HttpContext.Session.Remove("IsAuthenticated");
            return Json(new AuthResponse
            {
                Success = true,
                Authenticated = false,
                Message = "Успешный выход из системы"
            });
        }

        [HttpGet]
        public async Task<IActionResult> AuthStatus()
        {
            var isAuthenticated = await _jiraService.IsAuthenticatedAsync();
            var sessionAuth = HttpContext.Session.GetString("IsAuthenticated") == "true";

            return Json(new AuthResponse
            {
                Success = true,
                Authenticated = isAuthenticated && sessionAuth
            });
        }
    }
    
  
}
