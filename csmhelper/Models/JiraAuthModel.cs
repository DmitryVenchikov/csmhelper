using System.ComponentModel.DataAnnotations;

namespace csmhelper.Models
{
    public class JiraAuthModel
    {
        [Required(ErrorMessage = "Логин обязателен")]
        [Display(Name = "Логин")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Пароль обязателен")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; }
    }
}
