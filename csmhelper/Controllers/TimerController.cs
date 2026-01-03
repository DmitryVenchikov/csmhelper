using csmhelper.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace csmhelper.Controllers
{
    public class TimerController : Controller
    {
        private readonly ILogger<TimerController> _logger;

        public TimerController(ILogger<TimerController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            // Логируем обращение к главной странице таймера
            LogAccess("Index", "Home page accessed");
            return View();
        }

        public IActionResult Privacy()
        {
            // Логируем обращение к странице Privacy
            LogAccess("Privacy", "Privacy page accessed");
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Логируем ошибку
            var errorId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            LogError("Error", $"Error page accessed. RequestId: {errorId}");

            return View(new ErrorViewModel { RequestId = errorId });
        }

        // Метод для логирования доступа к страницам
        private void LogAccess(string pageName, string message)
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var logFilePath = Path.Combine(logDir, "timer_access.log");
                var now = DateTime.Now;
                var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Формируем запись лога
                var logEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] PAGE_ACCESS - " +
                              $"Page: {pageName}, " +
                              $"IP: {ipAddress}, " +
                              $"UserAgent: {userAgent}, " +
                              $"Message: {message}";

                System.IO.File.AppendAllText(logFilePath, logEntry + Environment.NewLine);

                // Также логируем в общий лог
                var commonLogPath = Path.Combine(logDir, "userlogs.txt");
                System.IO.File.AppendAllText(commonLogPath, logEntry + Environment.NewLine);

                _logger.LogInformation("Page {PageName} accessed by IP {IP}", pageName, ipAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging page access");
            }
        }

        // Метод для логирования ошибок
        private void LogError(string methodName, string errorMessage)
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var logFilePath = Path.Combine(logDir, "timer_errors.log");
                var now = DateTime.Now;
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Формируем запись лога ошибки
                var logEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] ERROR - " +
                              $"Method: {methodName}, " +
                              $"IP: {ipAddress}, " +
                              $"Message: {errorMessage}, " +
                              $"Path: {HttpContext.Request.Path}";

                System.IO.File.AppendAllText(logFilePath, logEntry + Environment.NewLine);

                // Также логируем в общий лог
                var commonLogPath = Path.Combine(logDir, "userlogs.txt");
                System.IO.File.AppendAllText(commonLogPath, logEntry + Environment.NewLine);

                _logger.LogError("Error in {MethodName}: {ErrorMessage}", methodName, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging error message");
            }
        }

        // Метод для сбора статистики по доступу (можно вызывать из других методов)
        private void TrackPageVisit(string pageName)
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var today = DateTime.Now.ToString("yyyy-MM-dd");
                var statsFile = Path.Combine(logDir, $"page_stats_{today}.txt");

                // Читаем существующую статистику
                Dictionary<string, int> pageStats = new Dictionary<string, int>();

                if (System.IO.File.Exists(statsFile))
                {
                    var lines = System.IO.File.ReadAllLines(statsFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int count))
                        {
                            pageStats[parts[0].Trim()] = count;
                        }
                    }
                }

                // Обновляем счетчик для страницы
                if (pageStats.ContainsKey(pageName))
                {
                    pageStats[pageName]++;
                }
                else
                {
                    pageStats[pageName] = 1;
                }

                // Сохраняем обновленную статистику
                var linesToWrite = pageStats.Select(kv => $"{kv.Key}: {kv.Value}");
                System.IO.File.WriteAllLines(statsFile, linesToWrite);

                // Логируем в общий лог при первом посещении страницы за день
                if (pageStats[pageName] == 1)
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PAGE_FIRST_VISIT - Page: {pageName} visited for the first time today";
                    var commonLogPath = Path.Combine(logDir, "userlogs.txt");
                    System.IO.File.AppendAllText(commonLogPath, logEntry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking page visit");
            }
        }

        // Пример API метода для получения статистики по страницам
        [HttpGet]
        [Route("timer/stats")]
        public IActionResult GetPageStats()
        {
            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                var statsFile = Path.Combine(logDir, $"page_stats_{today}.txt");

                Dictionary<string, int> pageStats = new Dictionary<string, int>();

                if (System.IO.File.Exists(statsFile))
                {
                    var lines = System.IO.File.ReadAllLines(statsFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int count))
                        {
                            pageStats[parts[0].Trim()] = count;
                        }
                    }
                }

                return Json(new
                {
                    Date = DateTime.Now,
                    TotalVisits = pageStats.Sum(x => x.Value),
                    PageStats = pageStats,
                    MostVisitedPage = pageStats.Any() ? pageStats.OrderByDescending(x => x.Value).First().Key : "None"
                });
            }
            catch (Exception ex)
            {
                LogError("GetPageStats", ex.Message);
                return Json(new { error = ex.Message });
            }
        }

        // Пример метода с параметрами
        [HttpGet]
        [Route("timer/custom/{id}")]
        public IActionResult CustomTimer(int id)
        {
            try
            {
                // Логируем вызов метода с параметром
                LogAccess("CustomTimer", $"Timer with ID {id} accessed");
                TrackPageVisit($"CustomTimer/{id}");

                // Логируем дополнительные данные
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                var customLogPath = Path.Combine(logDir, "timer_custom.log");
                var now = DateTime.Now;

                var logEntry = $"[{now:yyyy-MM-dd HH:mm:ss}] CUSTOM_TIMER - " +
                              $"ID: {id}, " +
                              $"User: {HttpContext.User.Identity?.Name ?? "Anonymous"}, " +
                              $"IP: {HttpContext.Connection.RemoteIpAddress}";

                System.IO.File.AppendAllText(customLogPath, logEntry + Environment.NewLine);

                // Возвращаем результат
                return Json(new { timerId = id, message = $"Timer {id} loaded" });
            }
            catch (Exception ex)
            {
                LogError("CustomTimer", $"Error loading timer {id}: {ex.Message}");
                return Json(new { error = ex.Message });
            }
        }
    }
}