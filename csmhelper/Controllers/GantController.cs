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
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Gant" });

            return View();
        }

        [HttpPost("Generate")]
        public async Task<IActionResult> Generate([FromBody] GantGenerateRequest request)
        {
            if (!IsAuthenticated())
                return Json(new GantGenerateResponse { Success = false, Error = "Требуется авторизация" });

            if (request == null)
                return Json(new GantGenerateResponse { Success = false, Error = "Некорректные данные запроса" });

            try
            {
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
