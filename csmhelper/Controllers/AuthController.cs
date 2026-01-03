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
                // Трекинг неудачной попытки (пустые данные)
                TrackFailedLogin(model.Username ?? "empty_username", "Empty username or password");

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
                    // Трекинг успешного входа
                    TrackUserLogin(model.Username, true);

                    HttpContext.Session.SetString("IsAuthenticated", "true");

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
                    // Трекинг неудачного входа (неверные учетные данные)
                    TrackFailedLogin(model.Username, "Invalid credentials");

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
                // Трекинг неудачного входа (ошибка сервера)
                TrackFailedLogin(model.Username ?? "unknown_user", $"Server error: {ex.Message}");

                _logger.LogError(ex, "Ошибка при аутентификации");
                return Json(new AuthResponse
                {
                    Success = false,
                    Authenticated = false,
                    Error = $"Ошибка аутентификации: {ex.Message}"
                });
            }
        }

        // Метод для трекинга успешных входов
        private void TrackUserLogin(string username, bool success = true)
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var logFilePath = Path.Combine(logDir, "userlogs.txt");
                var now = DateTime.Now;

                // Записываем успешный вход пользователя
                var loginEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] LOGIN_SUCCESS - User: {username}";
                System.IO.File.AppendAllText(logFilePath, loginEntry + Environment.NewLine);

                // Обновляем ежедневную статистику (только для успешных входов)
                UpdateDailyStats(logFilePath, username, now, success);

                // Обновляем ежемесячную статистику (только для успешных входов)
                UpdateMonthlyStats(logFilePath, username, now, success);

                _logger.LogInformation("Tracked successful login for user: {Username}", username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при записи метрик успешного входа пользователя");
            }
        }

        // Метод для трекинга неудачных попыток входа
        private void TrackFailedLogin(string username, string reason)
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var logFilePath = Path.Combine(logDir, "userlogs.txt");
                var failedLoginsFile = Path.Combine(logDir, "failed_logins.txt");
                var now = DateTime.Now;

                // Записываем неудачную попытку в основной лог
                var failedEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] LOGIN_FAILED - User: {username}, Reason: {reason}";
                System.IO.File.AppendAllText(logFilePath, failedEntry + Environment.NewLine);

                // Записываем неудачную попытку в отдельный файл для анализа
                var failedLoginEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] User: {username}, Reason: {reason}, IP: {HttpContext.Connection.RemoteIpAddress}";
                System.IO.File.AppendAllText(failedLoginsFile, failedLoginEntry + Environment.NewLine);

                // Обновляем статистику неудачных попыток
                UpdateFailedLoginStats(logDir, username, now, reason);

                _logger.LogWarning("Tracked failed login attempt for user: {Username}, Reason: {Reason}", username, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при записи метрик неудачного входа");
            }
        }

        // Обновление дневной статистики
        private void UpdateDailyStats(string filePath, string username, DateTime timestamp, bool success = true)
        {
            try
            {
                if (!success) return; // Для неудачных попыток не обновляем статистику успешных входов

                var today = timestamp.ToString("yyyy-MM-dd");
                var statsFile = Path.Combine(Path.GetDirectoryName(filePath), $"daily_stats_{today}.txt");

                Dictionary<string, int> dailyStats;

                if (System.IO.File.Exists(statsFile))
                {
                    var lines = System.IO.File.ReadAllLines(statsFile);
                    dailyStats = new Dictionary<string, int>();

                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int count))
                        {
                            dailyStats[parts[0].Trim()] = count;
                        }
                    }
                }
                else
                {
                    dailyStats = new Dictionary<string, int>();
                }

                // Обновляем счетчик для пользователя
                if (dailyStats.ContainsKey(username))
                {
                    dailyStats[username]++;
                }
                else
                {
                    dailyStats[username] = 1;
                }

                // Сохраняем обновленную статистику
                var linesToWrite = dailyStats.Select(kv => $"{kv.Key}: {kv.Value}");
                System.IO.File.WriteAllLines(statsFile, linesToWrite);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении дневной статистики");
            }
        }

        // Обновление месячной статистики
        private void UpdateMonthlyStats(string filePath, string username, DateTime timestamp, bool success = true)
        {
            try
            {
                if (!success) return; // Для неудачных попыток не обновляем статистику успешных входов

                var month = timestamp.ToString("yyyy-MM");
                var statsFile = Path.Combine(Path.GetDirectoryName(filePath), $"monthly_stats_{month}.txt");

                Dictionary<string, int> monthlyStats;

                if (System.IO.File.Exists(statsFile))
                {
                    var lines = System.IO.File.ReadAllLines(statsFile);
                    monthlyStats = new Dictionary<string, int>();

                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int count))
                        {
                            monthlyStats[parts[0].Trim()] = count;
                        }
                    }
                }
                else
                {
                    monthlyStats = new Dictionary<string, int>();
                }

                // Обновляем счетчик для пользователя
                if (monthlyStats.ContainsKey(username))
                {
                    monthlyStats[username]++;
                }
                else
                {
                    monthlyStats[username] = 1;
                }

                // Сохраняем обновленную статистику
                var linesToWrite = monthlyStats.Select(kv => $"{kv.Key}: {kv.Value}");
                System.IO.File.WriteAllLines(statsFile, linesToWrite);

                // Добавляем сводку в основной лог для новых пользователей
                if (monthlyStats[username] == 1)
                {
                    var summary = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] MONTHLY_STATS - New unique user for {month}: {username}";
                    System.IO.File.AppendAllText(filePath, summary + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении месячной статистики");
            }
        }

        // Обновление статистики неудачных попыток
        private void UpdateFailedLoginStats(string logDir, string username, DateTime timestamp, string reason)
        {
            try
            {
                var today = timestamp.ToString("yyyy-MM-dd");
                var failedStatsFile = Path.Combine(logDir, $"failed_stats_{today}.txt");

                Dictionary<string, FailedLoginInfo> failedStats;

                if (System.IO.File.Exists(failedStatsFile))
                {
                    var lines = System.IO.File.ReadAllLines(failedStatsFile);
                    failedStats = new Dictionary<string, FailedLoginInfo>();

                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length == 4)
                        {
                            failedStats[parts[0].Trim()] = new FailedLoginInfo
                            {
                                Username = parts[0].Trim(),
                                Attempts = int.Parse(parts[1].Trim()),
                                LastAttempt = DateTime.Parse(parts[2].Trim()),
                                LastReason = parts[3].Trim()
                            };
                        }
                    }
                }
                else
                {
                    failedStats = new Dictionary<string, FailedLoginInfo>();
                }

                // Обновляем статистику для пользователя
                if (failedStats.ContainsKey(username))
                {
                    failedStats[username].Attempts++;
                    failedStats[username].LastAttempt = timestamp;
                    failedStats[username].LastReason = reason;
                }
                else
                {
                    failedStats[username] = new FailedLoginInfo
                    {
                        Username = username,
                        Attempts = 1,
                        LastAttempt = timestamp,
                        LastReason = reason
                    };
                }

                // Сохраняем статистику
                var linesToWrite = failedStats.Values.Select(info =>
                    $"{info.Username} | {info.Attempts} | {info.LastAttempt:yyyy-MM-dd HH:mm:ss} | {info.LastReason}");

                System.IO.File.WriteAllLines(failedStatsFile, linesToWrite);

                // Если много неудачных попыток - логируем предупреждение
                if (failedStats[username].Attempts >= 5)
                {
                    var warning = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] SECURITY_WARNING - User {username} has {failedStats[username].Attempts} failed login attempts today";
                    System.IO.File.AppendAllText(Path.Combine(logDir, "userlogs.txt"), warning + Environment.NewLine);
                    _logger.LogWarning("Multiple failed login attempts for user: {Username}", username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статистики неудачных попыток");
            }
        }

        // Вспомогательный класс для статистики неудачных попыток
        private class FailedLoginInfo
        {
            public string Username { get; set; }
            public int Attempts { get; set; }
            public DateTime LastAttempt { get; set; }
            public string LastReason { get; set; }
        }

        // Метод для получения статистики (опционально)
        [HttpGet]
        [Route("auth/stats")]
        public IActionResult GetUserStats()
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                var today = DateTime.Now.ToString("yyyy-MM-dd");

                var dailyFile = Path.Combine(logDir, $"daily_stats_{today}.txt");
                var monthlyFile = Path.Combine(logDir, $"monthly_stats_{DateTime.Now:yyyy-MM}.txt");
                var failedFile = Path.Combine(logDir, $"failed_stats_{today}.txt");

                var result = new
                {
                    Date = DateTime.Now,
                    DailyStats = System.IO.File.Exists(dailyFile) ? System.IO.File.ReadAllLines(dailyFile) : new string[0],
                    MonthlyStats = System.IO.File.Exists(monthlyFile) ? System.IO.File.ReadAllLines(monthlyFile) : new string[0],
                    FailedStats = System.IO.File.Exists(failedFile) ? System.IO.File.ReadAllLines(failedFile) : new string[0],
                    TotalFailedToday = System.IO.File.Exists(failedFile) ?
                        System.IO.File.ReadAllLines(failedFile).Sum(line =>
                        {
                            var parts = line.Split('|');
                            return parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int count) ? count : 0;
                        }) : 0
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики");
                return Json(new { error = ex.Message });
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
