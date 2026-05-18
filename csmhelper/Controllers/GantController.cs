using csmhelper.Models;
using csmhelper.services;
using Microsoft.AspNetCore.Mvc;

namespace csmhelper.Controllers
{
    [Route("Gant")]
    public class GantController : Controller
    {
        private readonly IGantService _gantService;
        private readonly ILogger<GantController> _logger;

        public GantController(IGantService gantService, ILogger<GantController> logger)
        {
            _gantService = gantService;
            _logger = logger;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            if (!IsAuthenticated())
            {
                _logger.LogWarning("Попытка доступа к Gant без авторизации");
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Gant" });
            }

            return View();
        }

        [HttpPost("ExportRoadmap")]
        public IActionResult ExportRoadmap([FromBody] RoadmapExportRequest request)
        {
            if (!IsAuthenticated())
                return Unauthorized();

            if (request == null || request.Tasks == null || request.Tasks.Count == 0)
                return BadRequest("Нет данных для экспорта. Сначала постройте диаграмму.");

            try
            {
                var bytes = RoadmapExporter.Build(request.Tasks, request.Employees ?? new());
                var filename = $"roadmap-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка экспорта роадмэпа");
                return StatusCode(500, $"Ошибка экспорта: {ex.Message}");
            }
        }

        [HttpPost("Epics")]
        public async Task<IActionResult> Epics([FromBody] GantEpicsRequest request)
        {
            if (!IsAuthenticated())
                return Json(new GantEpicsResponse { Success = false, Error = "Требуется авторизация" });

            if (request == null)
                return Json(new GantEpicsResponse { Success = false, Error = "Некорректные данные запроса" });

            try
            {
                var result = await _gantService.GetEpicsAsync(request);
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке эпиков");
                return Json(new GantEpicsResponse { Success = false, Error = $"Ошибка: {ex.Message}" });
            }
        }

        [HttpPost("Generate")]
        public async Task<IActionResult> Generate([FromBody] GantGenerateRequest request)
        {
            if (!IsAuthenticated())
            {
                _logger.LogWarning("Попытка вызова Generate без авторизации");
                return Json(new GantGenerateResponse { Success = false, Error = "Требуется авторизация" });
            }

            if (request == null)
            {
                _logger.LogWarning("Получен пустой запрос");
                return Json(new GantGenerateResponse { Success = false, Error = "Некорректные данные запроса" });
            }

            try
            {
                _logger.LogInformation($"Получен запрос на генерацию. Проекты: {string.Join(",", request.Projects)}");
                var result = await _gantService.GenerateAsync(request);
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при генерации диаграммы Ганта");
                return Json(new GantGenerateResponse { Success = false, Error = $"Ошибка: {ex.Message}" });
            }
        }

        private bool IsAuthenticated() =>
            HttpContext.Session.GetString("IsAuthenticated") == "true";
    }
}