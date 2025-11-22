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

        [Display(Name = "Создать аналитику")]
        public bool CreateAnalysis { get; set; } = true;

        [Display(Name = "Создать разработку")]
        public bool CreateDev { get; set; } = true;

        [Display(Name = "Создать тестирование")]
        public bool CreateTest { get; set; } = true;
    }
}
