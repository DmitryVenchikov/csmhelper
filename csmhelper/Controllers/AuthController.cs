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

        // Страница авторизации
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login([FromBody] JiraAuthModel model)
        {
            // Добавим логирование для отладки
            Console.WriteLine($"Login attempt - Model is null: {model == null}");
            if (model != null)
            {
                Console.WriteLine($"Username: {model.Username}, Password: {(string.IsNullOrEmpty(model.Password) ? "empty" : "provided")}");
            }

            if (model == null)
            {
                return Json(new AuthResponse
                {
                    Success = false,
                    Error = "Данные не получены. Проверьте формат запроса."
                });
            }

            if (!ModelState.IsValid)
            {
                var errors = string.Join(", ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                Console.WriteLine($"Model validation errors: {errors}");

                return Json(new AuthResponse
                {
                    Success = false,
                    Error = $"Неверные данные для аутентификации: {errors}"
                });
            }

            // Проверяем конкретные поля
            if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
            {
                return Json(new AuthResponse
                {
                    Success = false,
                    Error = "Логин и пароль обязательны для заполнения"
                });
            }

            try
            {
                var success = await _jiraService.AuthenticateAsync(model.Username, model.Password);

                if (success)
                {
                    // Устанавливаем флаг авторизации в сессии
                    HttpContext.Session.SetString("IsAuthenticated", "true");

                    return Json(new AuthResponse
                    {
                        Success = true,
                        Authenticated = true,
                        Message = "Успешная аутентификация"
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
