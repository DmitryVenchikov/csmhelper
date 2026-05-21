using csmhelper.Controllers;
using csmhelper.Models;
using Microsoft.Extensions.FileSystemGlobbing;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Net.Mail;
using System;
using System.Text;
using System.Runtime.Intrinsics.X86;

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

                // Отдельно отслеживаем задачи по типу для правильной расстановки связей:
                // Аналитика → блокирует каждую задачу разработки
                // Задачи разработки (бэк, AM, AO) — параллельны, не блокируют друг друга
                // Каждая задача разработки → блокирует тест
                CreatedIssue? analysisIssue = null;
                var devIssues = new List<CreatedIssue>();
                CreatedIssue? testIssue = null;

                if (model.CreateAnalysis)
                {
                    analysisIssue = await CreateIssueAsync(model, "ANALYSIS", " - Аналитика",
                        "\n\nТип: Аналитическая задача\nТребуется провести анализ требований и составить ТЗ.");
                    if (analysisIssue != null) createdIssues.Add(analysisIssue);
                }

                if (model.CreateDevBackend)
                {
                    var issue = await CreateIssueAsync(model, "DEV BACK", " - Разработка (Backend)",
                        "\n\nТип: Задача на разработку (Backend)\nТребуется реализовать серверную часть согласно ТЗ.");
                    if (issue != null) { createdIssues.Add(issue); devIssues.Add(issue); }
                }

                if (model.CreateDevFrontAM)
                {
                    var issue = await CreateIssueAsync(model, "DEV FRONT AM", " - Разработка (Frontend AM)",
                        "\n\nТип: Задача на разработку (Frontend AM)\nТребуется реализовать клиентскую часть AM согласно ТЗ.");
                    if (issue != null) { createdIssues.Add(issue); devIssues.Add(issue); }
                }

                if (model.CreateDevFrontAO)
                {
                    var issue = await CreateIssueAsync(model, "DEV FRONT AO", " - Разработка (Frontend AO)",
                        "\n\nТип: Задача на разработку (Frontend AO)\nТребуется реализовать клиентскую часть AO согласно ТЗ.");
                    if (issue != null) { createdIssues.Add(issue); devIssues.Add(issue); }
                }

                if (model.CreateTest)
                {
                    testIssue = await CreateIssueAsync(model, "TEST", " - Тестирование",
                        "\n\nТип: Задача на тестирование\nТребуется провести тестирование реализованного функционала.");
                    if (testIssue != null) createdIssues.Add(testIssue);
                }

                // Расставляем блокирующие связи согласно правилам:
                var linksCreated = 0;

                // 1. Аналитика блокирует каждую задачу на разработку
                if (analysisIssue != null)
                {
                    foreach (var dev in devIssues)
                    {
                        var ok = await CreateIssueLinkAsync(analysisIssue.Key, dev.Key, "Blocks");
                        if (ok) linksCreated++;
                    }
                }

                // 2. Каждая задача на разработку блокирует тест
                if (testIssue != null)
                {
                    foreach (var dev in devIssues)
                    {
                        var ok = await CreateIssueLinkAsync(dev.Key, testIssue.Key, "Blocks");
                        if (ok) linksCreated++;
                    }

                    // Если задач на разработку нет, но есть аналитика — она блокирует тест напрямую
                    if (!devIssues.Any() && analysisIssue != null)
                    {
                        var ok = await CreateIssueLinkAsync(analysisIssue.Key, testIssue.Key, "Blocks");
                        if (ok) linksCreated++;
                    }
                }

                response.Success = true;
                response.TasksCreated = createdIssues.Count;
                response.LinksCreated = linksCreated;
                response.Tasks = createdIssues.Select(issue => new CreatedTask
                {
                    Key = issue.Key,
                    Summary = issue.Fields.Summary,
                    Url = $"{_jiraBaseUrl}/browse/{issue.Key}",
                    Type = issue.Fields.IssueType?.Name ?? "Task"
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
            // Двухшаговое создание задачи:
            // 1) POST /rest/api/2/issue — только базовые поля (без Epic Link),
            //    чтобы не получить 400 на проектах, где customfield_10008 нет на экране Create.
            // 2) Если задан EpicKey — PUT /rest/api/2/issue/{key} с customfield_10008 = ключ эпика.
            //    Если PUT провалится, задача уже создана; ошибка логируется, но создание не валим.

            var summary = $"[{prefix}] {model.Summary}{suffix}";
            var description = model.Description + descriptionSuffix;

            var issueData = new
            {
                fields = new
                {
                    project = new { key = model.ProjectKey },
                    summary = summary,
                    description = description,
                    issuetype = new { name = "Task" }
                }
            };

            var json = JsonConvert.SerializeObject(issueData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/rest/api/2/issue", content);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[CreateIssue] {prefix} failed: {response.StatusCode} — {errBody}");
                throw new Exception($"JIRA {response.StatusCode} при создании задачи [{prefix}]: {Truncate(errBody, 400)}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var createdIssue = JsonConvert.DeserializeObject<CreatedIssue>(responseContent);
            if (createdIssue == null)
                throw new Exception($"Не удалось распарсить ответ JIRA при создании [{prefix}]: {Truncate(responseContent, 200)}");

            // Защита от ситуации, когда десериализатор не создал Fields
            createdIssue.Fields ??= new IssueFields();
            createdIssue.Fields.Summary   = summary;
            createdIssue.Fields.IssueType = new IssueType { Name = prefix };

            // Привязка к эпику отдельным запросом
            if (!string.IsNullOrWhiteSpace(model.EpicKey))
            {
                var ok = await TrySetEpicLinkAsync(createdIssue.Key, model.EpicKey.Trim());
                if (!ok)
                    Console.WriteLine($"[CreateIssue] Эпик не привязан к {createdIssue.Key}");
            }

            return createdIssue;
        }

        private async Task<bool> TrySetEpicLinkAsync(string issueKey, string epicKey)
        {
            // Стратегия 1: Agile REST API — POST /rest/agile/1.0/epic/{epicKey}/issue
            // Надёжнее всего: не зависит от экранов редактирования и прав на customfield.
            try
            {
                var agilePayload = new { issues = new[] { issueKey } };
                var agileJson = JsonConvert.SerializeObject(agilePayload);
                var agileContent = new StringContent(agileJson, Encoding.UTF8, "application/json");
                var agileResponse = await _httpClient.PostAsync($"/rest/agile/1.0/epic/{epicKey}/issue", agileContent);

                if (agileResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[SetEpicLink] Agile API: {issueKey} → {epicKey} OK");
                    return true;
                }

                var agileBody = await agileResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[SetEpicLink] Agile API failed for {issueKey} → {epicKey}: {agileResponse.StatusCode} — {Truncate(agileBody, 300)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SetEpicLink] Agile API exception: {ex.Message}");
            }

            // Стратегия 2 (fallback): update-синтаксис через стандартный REST API
            // Работает даже если поле не на экране, но требует права на поле.
            try
            {
                var updatePayload = new
                {
                    update = new Dictionary<string, object>
                    {
                        ["customfield_10008"] = new[] { new { set = epicKey } }
                    }
                };
                var updateJson = JsonConvert.SerializeObject(updatePayload);
                var updateContent = new StringContent(updateJson, Encoding.UTF8, "application/json");
                var updateResponse = await _httpClient.PutAsync($"/rest/api/2/issue/{issueKey}", updateContent);

                if (updateResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[SetEpicLink] update-syntax: {issueKey} → {epicKey} OK");
                    return true;
                }

                var updateBody = await updateResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[SetEpicLink] update-syntax failed for {issueKey} → {epicKey}: {updateResponse.StatusCode} — {Truncate(updateBody, 300)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SetEpicLink] update-syntax exception: {ex.Message}");
            }

            // Стратегия 3 (последний вариант): fields-синтаксис (прежняя логика)
            try
            {
                var fieldsPayload = new
                {
                    fields = new Dictionary<string, object>
                    {
                        ["customfield_10008"] = epicKey
                    }
                };
                var fieldsJson = JsonConvert.SerializeObject(fieldsPayload);
                var fieldsContent = new StringContent(fieldsJson, Encoding.UTF8, "application/json");
                var fieldsResponse = await _httpClient.PutAsync($"/rest/api/2/issue/{issueKey}", fieldsContent);

                if (fieldsResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[SetEpicLink] fields-syntax: {issueKey} → {epicKey} OK");
                    return true;
                }

                var fieldsBody = await fieldsResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[SetEpicLink] fields-syntax failed for {issueKey} → {epicKey}: {fieldsResponse.StatusCode} — {Truncate(fieldsBody, 300)}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SetEpicLink] fields-syntax exception: {ex.Message}");
                return false;
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        // ─── Эпики проекта (для UI выбора при создании задач) ────────────

        public async Task<JiraEpicsResponse> GetEpicsByProjectAsync(string projectKey)
        {
            if (string.IsNullOrWhiteSpace(projectKey))
                return new JiraEpicsResponse { Success = false, Error = "Не указан ключ проекта" };

            if (!await IsAuthenticatedAsync())
                return new JiraEpicsResponse { Success = false, Error = "Требуется аутентификация" };

            try
            {
                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var jql = $"project = \"{projectKey.Trim()}\" AND issuetype = Epic AND status not in (\"Closed\", \"Done\", \"Resolved\")";
                // customfield_10009 — Epic Name в этой инсталляции (см. IssueFields.EpicName).
                var fields = "key,summary,status,customfield_10009";
                var response = await _httpClient.GetAsync(
                    $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields={fields}&maxResults=500");

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return new JiraEpicsResponse
                    {
                        Success = false,
                        Error = $"Jira API вернул {response.StatusCode}: {body.Substring(0, Math.Min(200, body.Length))}"
                    };
                }

                var contentStr = await response.Content.ReadAsStringAsync();
                var searchResult = JsonConvert.DeserializeObject<JiraSearchResult>(contentStr) ?? new JiraSearchResult();

                var epics = (searchResult.Issues ?? new List<JiraIssue>())
                    .Select(i => new JiraEpic
                    {
                        Key = i.Key ?? "",
                        Summary = i.Fields?.Summary ?? "",
                        EpicName = i.Fields?.EpicName ?? "",
                        Status = i.Fields?.Status?.Name ?? ""
                    })
                    .Where(e => !string.IsNullOrEmpty(e.Key))
                    .OrderBy(e => e.Key)
                    .ToList();

                return new JiraEpicsResponse { Success = true, Epics = epics };
            }
            catch (Exception ex)
            {
                return new JiraEpicsResponse { Success = false, Error = $"Ошибка получения эпиков: {ex.Message}" };
            }
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

        public async Task<List<JiraTask>> SearchTasksAsync(CleanupFilterModel filters)
        {
            if (!await IsAuthenticatedAsync())
            {
                throw new UnauthorizedAccessException("Требуется аутентификация");
            }

            var context = _httpContextAccessor.HttpContext;
            var username = context.Session.GetString("JiraUsername");
            var password = context.Session.GetString("JiraPassword");

            var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

            // Формируем JQL запрос
            var jql = BuildJqlQuery(filters);

            // Расширяем поля для получения полной информации
            var fields = "key,summary,status,created,updated,project,issuetype,assignee,reporter,priority,labels,resolution,duedate,timeestimate,timespent,customfield_10007,customfield_10008";

            var response = await _httpClient.GetAsync($"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields={fields}&maxResults=1000");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"JIRA API Response: {content}"); // Для отладки

                var searchResult = JsonConvert.DeserializeObject<JiraSearchResult>(content);

                var tasks = searchResult.Issues.Select(issue => new JiraTask
                {
                    Key = issue.Key,
                    Summary = issue.Fields.Summary ?? "Без названия",
                    Status = issue.Fields.Status?.Name ?? "Unknown",
                    Created = issue.Fields.Created ?? DateTime.Now.ToString("yyyy-MM-dd"),
                    Updated = issue.Fields.Updated ?? DateTime.Now.ToString("yyyy-MM-dd"),
                    Project = issue.Fields.Project?.Key ?? "Unknown",
                    IssueType = issue.Fields.IssueType?.Name ?? "Unknown",
                    Assignee = issue.Fields.Assignee?.DisplayName ?? "Unassigned",
                    Reporter = issue.Fields.Reporter?.DisplayName ?? "Unknown",
                    Priority = issue.Fields.Priority?.Name ?? "None",
                    Labels = issue.Fields.Labels ?? new List<string>(),
                    Resolution = issue.Fields.Resolution?.Name,
                    DueDate = issue.Fields.DueDate,
                    TimeEstimate = issue.Fields.TimeEstimate,
                    TimeSpent = issue.Fields.TimeSpent,
                    Sprint = GetSprintName(issue.Fields.Sprints),
                    EpicLink = issue.Fields.EpicLink,
                    Url = $"{_jiraBaseUrl}/browse/{issue.Key}"
                }).ToList();

                Console.WriteLine($"Mapped {tasks.Count} tasks"); // Для отладки
                return tasks;
            }

            throw new Exception($"Ошибка при поиске задач: {response.StatusCode}");
        }
        public async Task<ProcessResult> TransitionIssueAsync(string issueKey, string targetStatus, string resolution)
        {
            try
            {
                Console.WriteLine($"Starting transition for {issueKey} to {targetStatus} with resolution {resolution}");

                var transitions = await GetTransitionsAsync(issueKey);

                Console.WriteLine($"Available transitions for {issueKey}: {string.Join(", ", transitions.Select(t => $"{t.Name} -> {t.To.Name}"))}");

                // Ищем переход в целевой статус (нечеткое сравнение)
                var targetTransition = transitions.FirstOrDefault(t =>
                    t.To.Name.Equals(targetStatus, StringComparison.OrdinalIgnoreCase) ||
                    t.To.Name.Replace(" ", "").Equals(targetStatus.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals(targetStatus, StringComparison.OrdinalIgnoreCase));

                if (targetTransition == null)
                {
                    // Пробуем найти переход по ключевым словам
                    var closedKeywords = new[] { "close", "done", "resolve", "complete", "finished" };
                    targetTransition = transitions.FirstOrDefault(t =>
                        closedKeywords.Any(keyword =>
                            t.To.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

                    if (targetTransition == null)
                    {
                        return new ProcessResult
                        {
                            Key = issueKey,
                            Success = false,
                            Action = "transition",
                            Message = $"Не найден переход в статус '{targetStatus}'",
                            ErrorDetails = $"Доступные переходы: {string.Join(", ", transitions.Select(t => $"{t.Name} -> {t.To.Name}"))}"
                        };
                    }

                    Console.WriteLine($"Found transition by keyword: {targetTransition.Name} -> {targetTransition.To.Name}");
                }

                Console.WriteLine($"Using transition: {targetTransition.Name} (ID: {targetTransition.Id}) to status: {targetTransition.To.Name}");

                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                // Подготавливаем данные для перехода
                var transitionData = new Dictionary<string, object>
                {
                    ["transition"] = new { id = targetTransition.Id }
                };

                // Добавляем resolution если указан и если переход в закрытый статус
                if (!string.IsNullOrEmpty(resolution) &&
                    (targetTransition.To.Name.Contains("close", StringComparison.OrdinalIgnoreCase) ||
                     targetTransition.To.Name.Contains("done", StringComparison.OrdinalIgnoreCase) ||
                     targetTransition.To.Name.Contains("resolve", StringComparison.OrdinalIgnoreCase)))
                {
                    transitionData["fields"] = new { resolution = new { name = resolution } };
                    Console.WriteLine($"Adding resolution: {resolution}");
                }

                var json = JsonConvert.SerializeObject(transitionData);
                Console.WriteLine($"Transition request data: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"/rest/api/2/issue/{issueKey}/transitions", content);

                Console.WriteLine($"Transition response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    return new ProcessResult
                    {
                        Key = issueKey,
                        Success = true,
                        Action = "transitioned",
                        Message = $"Задача переведена в статус '{targetTransition.To.Name}'",
                        NewStatus = targetTransition.To.Name,
                        Resolution = resolution
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Transition error response: {errorContent}");

                    return new ProcessResult
                    {
                        Key = issueKey,
                        Success = false,
                        Action = "transition",
                        Message = $"Ошибка при переводе статуса: {response.StatusCode}",
                        ErrorDetails = errorContent
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Transition exception: {ex}");
                return new ProcessResult
                {
                    Key = issueKey,
                    Success = false,
                    Action = "transition",
                    Message = $"Исключение при переводе статуса: {ex.Message}",
                    ErrorDetails = ex.ToString()
                };
            }
        }
        public async Task<List<Transition>> GetTransitionsAsync(string issueKey)
        {
            try
            {
                if (!await IsAuthenticatedAsync())
                {
                    throw new UnauthorizedAccessException("Требуется аутентификация");
                }

                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var response = await _httpClient.GetAsync($"/rest/api/2/issue/{issueKey}/transitions?expand=transitions.fields");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var transitionsResponse = JsonConvert.DeserializeObject<TransitionsResponse>(content);
                    return transitionsResponse.Transitions ?? new List<Transition>();
                }
                else
                {
                    Console.WriteLine($"Error getting transitions for {issueKey}: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error content: {errorContent}");
                    return new List<Transition>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception getting transitions for {issueKey}: {ex}");
                return new List<Transition>();
            }
        }
        public async Task<string> GetCurrentStatusAsync(string issueKey)
        {
            try
            {
                if (!await IsAuthenticatedAsync())
                {
                    throw new UnauthorizedAccessException("Требуется аутентификация");
                }

                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var response = await _httpClient.GetAsync($"/rest/api/2/issue/{issueKey}?fields=status");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var issue = JsonConvert.DeserializeObject<JiraIssue>(content);
                    return issue.Fields.Status?.Name ?? "Unknown";
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting current status for {issueKey}: {ex}");
                return "Unknown";
            }
        }
        public async Task<List<ProcessResult>> ProcessTasksAsync(List<string> taskKeys, string actionType, string targetStatus, string resolution)
        {
            var results = new List<ProcessResult>();

            // Разбиваем на батчи по 3 задачи
            var batches = new List<List<string>>();
            for (int i = 0; i < taskKeys.Count; i += 3)
            {
                batches.Add(taskKeys.Skip(i).Take(3).ToList());
            }

            foreach (var batch in batches)
            {
                var batchTasks = new List<Task<ProcessResult>>();

                foreach (var taskKey in batch)
                {
                    // Получаем текущий статус для отладки
                    var currentStatus = await GetCurrentStatusAsync(taskKey);
                    Console.WriteLine($"Task {taskKey} current status: {currentStatus}");

                    if (actionType == "delete")
                    {
                        batchTasks.Add(DeleteIssueAsync(taskKey));
                    }
                    else if (actionType == "transition")
                    {
                        batchTasks.Add(TransitionIssueAsync(taskKey, targetStatus, resolution));
                    }
                }

                // Ожидаем завершения всех задач в батче
                var batchResults = await Task.WhenAll(batchTasks);
                results.AddRange(batchResults);

                // Пауза между батчами чтобы не перегружать JIRA
                if (batch != batches.Last())
                {
                    await Task.Delay(500);
                }
            }

            return results;
        }
        public async Task<ProcessResult> DeleteIssueAsync(string issueKey)
        {
            try
            {
                Console.WriteLine($"Starting delete for issue: {issueKey}");

                var context = _httpContextAccessor.HttpContext;
                var username = context.Session.GetString("JiraUsername");
                var password = context.Session.GetString("JiraPassword");

                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var response = await _httpClient.DeleteAsync($"/rest/api/2/issue/{issueKey}");

                Console.WriteLine($"Delete response status for {issueKey}: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    return new ProcessResult
                    {
                        Key = issueKey,
                        Success = true,
                        Action = "deleted",
                        Message = "Задача успешно удалена"
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Delete error response for {issueKey}: {errorContent}");

                    return new ProcessResult
                    {
                        Key = issueKey,
                        Success = false,
                        Action = "delete",
                        Message = $"Ошибка при удалении: {response.StatusCode}",
                        ErrorDetails = errorContent
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delete exception for {issueKey}: {ex}");
                return new ProcessResult
                {
                    Key = issueKey,
                    Success = false,
                    Action = "delete",
                    Message = $"Исключение при удалении: {ex.Message}",
                    ErrorDetails = ex.ToString()
                };
            }
        }
        private string BuildJqlQuery(CleanupFilterModel filters)
        {
            var jqlParts = new List<string>();

            if (filters.Projects?.Any() == true)
            {
                jqlParts.Add($"project in ({string.Join(", ", filters.Projects.Select(p => $"\"{p}\""))})");
            }

            if (filters.ExcludedProjects?.Any() == true)
            {
                jqlParts.Add($"project not in ({string.Join(", ", filters.ExcludedProjects.Select(p => $"\"{p}\""))})");
            }

            if (filters.Statuses?.Any() == true)
            {
                jqlParts.Add($"status in ({string.Join(", ", filters.Statuses.Select(s => $"\"{s}\""))})");
            }

            if (filters.ExcludedStatuses?.Any() == true)
            {
                jqlParts.Add($"status not in ({string.Join(", ", filters.ExcludedStatuses.Select(s => $"\"{s}\""))})");
            }

            if (filters.CleanupCreatedDate.HasValue)
            {
                jqlParts.Add($"created < \"{filters.CleanupCreatedDate.Value:yyyy-MM-dd}\"");
            }

            if (filters.ExcludedCreatedDate.HasValue)
            {
                jqlParts.Add($"created >= \"{filters.ExcludedCreatedDate.Value:yyyy-MM-dd}\"");
            }

            if (filters.CleanupUpdatedDate.HasValue)
            {
                jqlParts.Add($"updated < \"{filters.CleanupUpdatedDate.Value:yyyy-MM-dd}\"");
            }

            if (filters.ExcludedUpdatedDate.HasValue)
            {
                jqlParts.Add($"updated >= \"{filters.ExcludedUpdatedDate.Value:yyyy-MM-dd}\"");
            }

            if (!string.IsNullOrEmpty(filters.SprintFilter))
            {
                jqlParts.Add($"Sprint = \"{filters.SprintFilter}\"");
            }
            else
            {
                jqlParts.Add("Sprint IS EMPTY");
            }

            if (filters.ExcludedSprints?.Any() == true)
            {
                jqlParts.Add($"Sprint not in ({string.Join(", ", filters.ExcludedSprints.Select(s => $"\"{s}\""))})");
            }

            return string.Join(" AND ", jqlParts);
        }
         
        // Добавьте этот метод в класс JiraService
        private string GetSprintName(List<Sprint> sprints)
        {
            if (sprints == null || sprints.Count == 0)
                return "Без спринта";

            // Берем первый спринт (обычно у задачи один активный спринт)
            var sprint = sprints.First();
            return sprint.Name ?? "Без спринта";
        }
    }


    // Вспомогательные классы для десериализации ответов JIRA
    public class CreatedIssue
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public IssueFields Fields { get; set; } = new IssueFields();
    }
    public class CreatedIssueFields
    {
        public string Summary { get; set; }
        public string IssueType { get; set; } // Должно быть string, а не IssueType
    }

    public class JiraIssue
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("fields")]
        public IssueFields Fields { get; set; } = new IssueFields();
    }

    public class IssueFields
    {
        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("status")]
        public Status Status { get; set; } = new Status();

        [JsonProperty("project")]
        public Project Project { get; set; } = new Project();

        [JsonProperty("issuetype")]
        public IssueType IssueType { get; set; } = new IssueType();

        [JsonProperty("created")]
        public string Created { get; set; }

        [JsonProperty("updated")]
        public string Updated { get; set; }

        [JsonProperty("creator")]
        public User Creator { get; set; } = new User();

        [JsonProperty("reporter")]
        public User Reporter { get; set; } = new User();

        [JsonProperty("assignee")]
        public User Assignee { get; set; } = new User();

        [JsonProperty("priority")]
        public Priority Priority { get; set; } = new Priority();

        [JsonProperty("labels")]
        public List<string> Labels { get; set; } = new List<string>();

        [JsonProperty("components")]
        public List<Component> Components { get; set; } = new List<Component>();

        [JsonProperty("customfield_10007")] // Sprint field in JIRA
        public List<Sprint> Sprints { get; set; } = new List<Sprint>();

        [JsonProperty("customfield_10008")] // Epic Link
        public string EpicLink { get; set; }

        [JsonProperty("customfield_10009")] // Epic Name
        public string EpicName { get; set; }

        [JsonProperty("resolution")]
        public Resolution Resolution { get; set; }

        [JsonProperty("resolutiondate")]
        public string ResolutionDate { get; set; }

        [JsonProperty("duedate")]
        public string DueDate { get; set; }

        [JsonProperty("timeestimate")]
        public long? TimeEstimate { get; set; }

        [JsonProperty("timeoriginalestimate")]
        public long? TimeOriginalEstimate { get; set; }

        [JsonProperty("timespent")]
        public long? TimeSpent { get; set; }

        [JsonProperty("progress")]
        public Progress Progress { get; set; } = new Progress();

        [JsonProperty("aggregateprogress")]
        public Progress AggregateProgress { get; set; } = new Progress();

        [JsonProperty("workratio")]
        public long? WorkRatio { get; set; }

        [JsonProperty("environment")]
        public string Environment { get; set; }

        [JsonProperty("lastViewed")]
        public string LastViewed { get; set; }

        [JsonProperty("votes")]
        public Votes Votes { get; set; } = new Votes();

        [JsonProperty("watches")]
        public Watches Watches { get; set; } = new Watches();

        [JsonProperty("attachment")]
        public List<Attachment> Attachments { get; set; } = new List<Attachment>();

        [JsonProperty("comment")]
        public CommentList Comments { get; set; } = new CommentList();

        [JsonProperty("worklog")]
        public WorklogList Worklogs { get; set; } = new WorklogList();

        [JsonProperty("subtasks")]
        public List<JiraIssue> Subtasks { get; set; } = new List<JiraIssue>();

        [JsonProperty("issuelinks")]
        public List<IssueLink> IssueLinks { get; set; } = new List<IssueLink>();

        [JsonProperty("fixVersions")]
        public List<Version> FixVersions { get; set; } = new List<Version>();

        [JsonProperty("versions")]
        public List<Version> Versions { get; set; } = new List<Version>();
    }
    public class Status
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("statusCategory")]
        public StatusCategory StatusCategory { get; set; } = new StatusCategory();
    }

    public class StatusCategory
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("colorName")]
        public string ColorName { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class Project
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("projectTypeKey")]
        public string ProjectTypeKey { get; set; }

        [JsonProperty("avatarUrls")]
        public AvatarUrls AvatarUrls { get; set; } = new AvatarUrls();
    }

    public class IssueType
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("subtask")]
        public bool Subtask { get; set; }

        [JsonProperty("avatarId")]
        public int? AvatarId { get; set; }
    }

    public class User
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("accountId")]
        public string AccountId { get; set; }

        [JsonProperty("emailAddress")]
        public string EmailAddress { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("timeZone")]
        public string TimeZone { get; set; }

        [JsonProperty("avatarUrls")]
        public AvatarUrls AvatarUrls { get; set; } = new AvatarUrls();
    }

    public class AvatarUrls
    {
        [JsonProperty("48x48")]
        public string Size48 { get; set; }

        [JsonProperty("24x24")]
        public string Size24 { get; set; }

        [JsonProperty("16x16")]
        public string Size16 { get; set; }

        [JsonProperty("32x32")]
        public string Size32 { get; set; }
    }

    public class Priority
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }

    public class Component
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class Sprint
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("startDate")]
        public string StartDate { get; set; }

        [JsonProperty("endDate")]
        public string EndDate { get; set; }

        [JsonProperty("completeDate")]
        public string CompleteDate { get; set; }

        [JsonProperty("originBoardId")]
        public int OriginBoardId { get; set; }

        [JsonProperty("goal")]
        public string Goal { get; set; }
    }

    public class Resolution
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class Progress
    {
        [JsonProperty("progress")]
        public long ProgressValue { get; set; }

        [JsonProperty("total")]
        public long Total { get; set; }

        [JsonProperty("percent")]
        public int Percent { get; set; }
    }

    public class Votes
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("votes")]
        public int VotesCount { get; set; }

        [JsonProperty("hasVoted")]
        public bool HasVoted { get; set; }
    }

    public class Watches
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("watchCount")]
        public int WatchCount { get; set; }

        [JsonProperty("isWatching")]
        public bool IsWatching { get; set; }
    }

    public class Attachment
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("author")]
        public User Author { get; set; } = new User();

        [JsonProperty("created")]
        public string Created { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }
    }

    public class CommentList
    {
        [JsonProperty("comments")]
        public List<Comment> Comments { get; set; } = new List<Comment>();

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("startAt")]
        public int StartAt { get; set; }
    }

    public class Comment
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("author")]
        public User Author { get; set; } = new User();

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("updateAuthor")]
        public User UpdateAuthor { get; set; } = new User();

        [JsonProperty("created")]
        public string Created { get; set; }

        [JsonProperty("updated")]
        public string Updated { get; set; }

        [JsonProperty("visibility")]
        public Visibility Visibility { get; set; } = new Visibility();
    }

    public class Visibility
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }

    public class WorklogList
    {
        [JsonProperty("worklogs")]
        public List<Worklog> Worklogs { get; set; } = new List<Worklog>();

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("startAt")]
        public int StartAt { get; set; }
    }

    public class Worklog
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("author")]
        public User Author { get; set; } = new User();

        [JsonProperty("updateAuthor")]
        public User UpdateAuthor { get; set; } = new User();

        [JsonProperty("comment")]
        public string Comment { get; set; }

        [JsonProperty("created")]
        public string Created { get; set; }

        [JsonProperty("updated")]
        public string Updated { get; set; }

        [JsonProperty("started")]
        public string Started { get; set; }

        [JsonProperty("timeSpent")]
        public string TimeSpent { get; set; }

        [JsonProperty("timeSpentSeconds")]
        public long TimeSpentSeconds { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("issueId")]
        public string IssueId { get; set; }
    }

    public class IssueLink
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("type")]
        public LinkType Type { get; set; } = new LinkType();

        [JsonProperty("inwardIssue")]
        public JiraIssue InwardIssue { get; set; }

        [JsonProperty("outwardIssue")]
        public JiraIssue OutwardIssue { get; set; }
    }

    public class LinkType
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("inward")]
        public string Inward { get; set; }

        [JsonProperty("outward")]
        public string Outward { get; set; }
    }

    public class Version
    {
        [JsonProperty("self")]
        public string Self { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("archived")]
        public bool Archived { get; set; }

        [JsonProperty("released")]
        public bool Released { get; set; }

        [JsonProperty("releaseDate")]
        public string ReleaseDate { get; set; }

        [JsonProperty("userReleaseDate")]
        public string UserReleaseDate { get; set; }

        [JsonProperty("projectId")]
        public int ProjectId { get; set; }
    }

    public class JiraSearchResult
    {
        [JsonProperty("expand")]
        public string Expand { get; set; }

        [JsonProperty("startAt")]
        public int StartAt { get; set; }

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("issues")]
        public List<JiraIssue> Issues { get; set; } = new List<JiraIssue>();
    }

    public class Transition
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("to")]
        public Status To { get; set; } = new Status();

        [JsonProperty("hasScreen")]
        public bool HasScreen { get; set; }

        [JsonProperty("isGlobal")]
        public bool IsGlobal { get; set; }

        [JsonProperty("isInitial")]
        public bool IsInitial { get; set; }

        [JsonProperty("isAvailable")]
        public bool IsAvailable { get; set; }

        [JsonProperty("isConditional")]
        public bool IsConditional { get; set; }

        [JsonProperty("fields")]
        public Dictionary<string, FieldMetadata> Fields { get; set; } = new Dictionary<string, FieldMetadata>();
    }

    public class FieldMetadata
    {
        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("schema")]
        public FieldSchema Schema { get; set; } = new FieldSchema();

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("hasDefaultValue")]
        public bool HasDefaultValue { get; set; }

        [JsonProperty("operations")]
        public List<string> Operations { get; set; } = new List<string>();

        [JsonProperty("allowedValues")]
        public List<object> AllowedValues { get; set; } = new List<object>();
    }

    public class FieldSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("items")]
        public string Items { get; set; }

        [JsonProperty("system")]
        public string System { get; set; }

        [JsonProperty("custom")]
        public string Custom { get; set; }

        [JsonProperty("customId")]
        public long CustomId { get; set; }
    }
    public class JiraTask
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("created")]
        public string Created { get; set; }

        [JsonProperty("updated")]
        public string Updated { get; set; }

        [JsonProperty("project")]
        public string Project { get; set; }

        [JsonProperty("issueType")]
        public string IssueType { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sprint")]
        public string Sprint { get; set; }

        [JsonProperty("epicLink")]
        public string EpicLink { get; set; }

        [JsonProperty("assignee")]
        public string Assignee { get; set; }

        [JsonProperty("reporter")]
        public string Reporter { get; set; }

        [JsonProperty("priority")]
        public string Priority { get; set; }

        [JsonProperty("labels")]
        public List<string> Labels { get; set; } = new List<string>();

        [JsonProperty("resolution")]
        public string Resolution { get; set; }

        [JsonProperty("dueDate")]
        public string DueDate { get; set; }

        [JsonProperty("timeEstimate")]
        public long? TimeEstimate { get; set; }

        [JsonProperty("timeSpent")]
        public long? TimeSpent { get; set; }
    }


    public class ProcessResult
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("oldStatus")]
        public string OldStatus { get; set; }

        [JsonProperty("newStatus")]
        public string NewStatus { get; set; }

        [JsonProperty("resolution")]
        public string Resolution { get; set; }

        [JsonProperty("errorDetails")]
        public string ErrorDetails { get; set; }
    }
    public class TransitionsResponse
    {
        [JsonProperty("expand")]
        public string Expand { get; set; }

        [JsonProperty("transitions")]
        public List<Transition> Transitions { get; set; } = new List<Transition>();
    }
}
