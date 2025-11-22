using csmhelper.Models;
using Newtonsoft.Json;
using System.Text;

namespace csmhelper.services
{
    public class JiraService : IJiraService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _jiraBaseUrl = "https://jira.moscow.alfaintra.net";

        public JiraService(HttpClient httpClient, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _httpClient.BaseAddress = new Uri(_jiraBaseUrl);
        }

        public async Task<bool> AuthenticateAsync(string username, string password)
        {
            try
            {
                // Проверяем подключение к JIRA
                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                // Тестовый запрос для проверки аутентификации
                var response = await _httpClient.GetAsync("/rest/api/2/myself");

                if (response.IsSuccessStatusCode)
                {
                    // Сохраняем учетные данные в сессии
                    var context = _httpContextAccessor.HttpContext;
                    context.Session.SetString("JiraUsername", username);
                    context.Session.SetString("JiraPassword", password);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                Console.WriteLine($"Ошибка аутентификации: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var context = _httpContextAccessor.HttpContext;
            var username = context.Session.GetString("JiraUsername");
            var password = context.Session.GetString("JiraPassword");

            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        }

        public async Task LogoutAsync()
        {
            var context = _httpContextAccessor.HttpContext;
            context.Session.Remove("JiraUsername");
            context.Session.Remove("JiraPassword");
        }

        public async Task<TaskCreationResponse> CreateLinkedTasksAsync(TaskCreationModel model)
        {
            var response = new TaskCreationResponse();

            try
            {
                if (!await IsAuthenticatedAsync())
                {
                    response.Success = false;
                    response.Error = "Требуется аутентификация";
                    return response;
                }

                // Получаем учетные данные из сессии
                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                // Создаем задачи через JIRA REST API
                var createdIssues = new List<CreatedIssue>();

                // Создаем задачи в зависимости от выбранных чекбоксов
                if (model.CreateAnalysis)
                {
                    var analysisIssue = await CreateIssueAsync(model, "ANALYSIS", " - Аналитика",
                        "\n\nТип: Аналитическая задача\nТребуется провести анализ требований и составить ТЗ.");
                    if (analysisIssue != null) createdIssues.Add(analysisIssue);
                }

                if (model.CreateDev)
                {
                    var devIssue = await CreateIssueAsync(model, "DEV", " - Разработка",
                        "\n\nТип: Задача на разработку\nТребуется реализовать функционал согласно ТЗ.");
                    if (devIssue != null) createdIssues.Add(devIssue);
                }

                if (model.CreateTest)
                {
                    var testIssue = await CreateIssueAsync(model, "TEST", " - Тестирование",
                        "\n\nТип: Задача на тестирование\nТребуется провести тестирование реализованного функционала.");
                    if (testIssue != null) createdIssues.Add(testIssue);
                }

                // Создаем блокирующие связи
                var linksCreated = 0;
                for (int i = 0; i < createdIssues.Count - 1; i++)
                {
                    var linkSuccess = await CreateIssueLinkAsync(createdIssues[i].Key, createdIssues[i + 1].Key, "Blocks");
                    if (linkSuccess) linksCreated++;
                }

                response.Success = true;
                response.TasksCreated = createdIssues.Count;
                response.LinksCreated = linksCreated;
                response.Tasks = createdIssues.Select(issue => new CreatedTask
                {
                    Key = issue.Key,
                    Summary = issue.Fields.Summary,
                    Url = $"{_jiraBaseUrl}/browse/{issue.Key}",
                    Type = issue.Fields.IssueType
                }).ToList();

                response.Message = $"Успешно создано {createdIssues.Count} задач с {linksCreated} блокирующими связями";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Error = $"Ошибка при создании задач: {ex.Message}";
            }

            return response;
        }

        private async Task<CreatedIssue> CreateIssueAsync(TaskCreationModel model, string prefix, string suffix, string descriptionSuffix)
        {
            var issueData = new
            {
                fields = new
                {
                    project = new { key = model.ProjectKey },
                    summary = $"{prefix}: {model.Summary}{suffix}",
                    description = model.Description + descriptionSuffix,
                    issuetype = new { name = "Task" }
                }
            };

            var json = JsonConvert.SerializeObject(issueData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/rest/api/2/issue", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var createdIssue = JsonConvert.DeserializeObject<CreatedIssue>(responseContent);
                createdIssue.Fields.IssueType = prefix;
                return createdIssue;
            }

            return null;
        }

        private async Task<bool> CreateIssueLinkAsync(string inwardIssue, string outwardIssue, string linkType)
        {
            var linkData = new
            {
                type = new { name = linkType },
                inwardIssue = new { key = inwardIssue },
                outwardIssue = new { key = outwardIssue }
            };

            var json = JsonConvert.SerializeObject(linkData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/rest/api/2/issueLink", content);
            return response.IsSuccessStatusCode;
        }
    }

    // Вспомогательные классы для десериализации ответов JIRA
    public class CreatedIssue
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public IssueFields Fields { get; set; } = new IssueFields();
    }

    public class IssueFields
    {
        public string Summary { get; set; }
        public string IssueType { get; set; }
    }
}
