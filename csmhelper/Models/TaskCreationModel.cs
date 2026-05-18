using System.ComponentModel.DataAnnotations;

namespace csmhelper.Models
{
    public class TaskCreationModel
    {
        [Required(ErrorMessage = "Ключ проекта обязателен")]
        [Display(Name = "Ключ проекта")]
        public string ProjectKey { get; set; }

        [Required(ErrorMessage = "Заголовок обязателен")]
        [Display(Name = "Заголовок задачи")]
        public string Summary { get; set; }

        [Required(ErrorMessage = "Описание обязательно")]
        [Display(Name = "Описание задачи")]
        public string Description { get; set; }

        /// <summary>
        /// Ключ эпика (например "PROJ-123"), к которому будут прикреплены все созданные задачи.
        /// Обязателен — все задачи всегда создаются в рамках эпика.
        /// </summary>
        [Required(ErrorMessage = "Эпик обязателен — выберите его из списка")]
        [Display(Name = "Эпик")]
        public string EpicKey { get; set; }

        [Display(Name = "Создать аналитику")]
        public bool CreateAnalysis { get; set; } = true;

        /// <summary>Разработка — backend</summary>
        [Display(Name = "Создать разработку (Backend)")]
        public bool CreateDevBackend { get; set; } = true;

        /// <summary>Разработка — Frontend AM</summary>
        [Display(Name = "Создать разработку (Frontend AM)")]
        public bool CreateDevFrontAM { get; set; } = false;

        /// <summary>Разработка — Frontend AO</summary>
        [Display(Name = "Создать разработку (Frontend AO)")]
        public bool CreateDevFrontAO { get; set; } = false;

        [Display(Name = "Создать тестирование")]
        public bool CreateTest { get; set; } = true;
    }

    // ─── Epics для UI создания задач ─────────────────────────────

    public class JiraEpicsRequest
    {
        [Required]
        public string ProjectKey { get; set; } = "";
    }

    public class JiraEpic
    {
        public string Key { get; set; } = "";
        public string Summary { get; set; } = "";
        public string EpicName { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class JiraEpicsResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<JiraEpic> Epics { get; set; } = new();
    }
}
