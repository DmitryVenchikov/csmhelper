using csmhelper.Models;
using Newtonsoft.Json;
using System.Text;

namespace csmhelper.services
{
    public class GantService : IGantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GantService> _logger;

        public GantService(
            IHttpContextAccessor httpContextAccessor,
            IHttpClientFactory httpClientFactory,
            ILogger<GantService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<GantGenerateResponse> GenerateAsync(GantGenerateRequest request)
        {
            // ── 1. Get credentials from session ──────────────────
            var ctx = _httpContextAccessor.HttpContext!;
            var username = ctx.Session.GetString("JiraUsername");
            var password = ctx.Session.GetString("JiraPassword");

            _logger.LogInformation($"Получены учетные данные: username={username}, password={(password != null ? "***" : "null")}");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("Сессия Jira пуста");
                return Fail("Сессия Jira истекла. Пожалуйста, войдите заново.");
            }

            // ── 2. Build employees ────────────────────────────────
            var employees = new List<GantEmployee>();
            foreach (var e in request.Employees)
            {
                var role = ParseRole(e.Role);
                if (role == null)
                {
                    _logger.LogWarning($"Неизвестная роль: {e.Role}");
                    return Fail($"Неизвестная роль: {e.Role}");
                }

                var vacations = (e.Vacations ?? new List<GantVacationInput>())
                    .Where(v => v.EndDate.Date >= v.StartDate.Date)
                    .Select(v => (Start: v.StartDate.Date, End: v.EndDate.Date))
                    .ToList();

                employees.Add(new GantEmployee
                {
                    Name = e.Name.Trim(),
                    Role = role.Value,
                    HoursPerDay = e.HoursPerDay,
                    WorkStart = ParseTime(e.WorkStart),
                    WorkEnd = ParseTime(e.WorkEnd),
                    LunchStart = ParseTime(e.LunchStart),
                    LunchEnd = ParseTime(e.LunchEnd),
                    Vacations = vacations,
                });
            }

            if (!employees.Any())
                return Fail("Добавьте хотя бы одного сотрудника.");

            // ── 3. Fetch tasks from Jira ──────────────────────────
            List<RawJiraTask> rawTasks;
            try
            {
                _logger.LogInformation($"Запрос к Jira: server={request.JiraServer}, projects={string.Join(",", request.Projects)}, epics={string.Join(",", request.EpicKeys ?? new())}");
                rawTasks = await FetchJiraTasksAsync(
                    request.JiraServer, username, password,
                    request.Projects, request.VerifySsl, request.Schedule,
                    request.EpicKeys ?? new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Jira fetch failed");
                return Fail($"Ошибка получения задач из Jira: {ex.Message}");
            }

            _logger.LogInformation($"Получено задач из Jira: {rawTasks.Count}");

            if (!rawTasks.Any())
            {
                _logger.LogWarning("Нет задач, подходящих под критерии фильтрации");
                return new GantGenerateResponse
                {
                    Success = true,
                    Stats = new GantStats(),
                    Scheduled = new(),
                };
            }

            // ── 4. Build schedule ─────────────────────────────────
            var builder = new ScheduleBuilder(employees, request.Schedule);
            var scheduled = builder.Build(rawTasks);

            var criticalKeys = builder.CriticalPath.Select(t => t.Key).ToList();

            return new GantGenerateResponse
            {
                Success = true,
                Scheduled = scheduled,
                CriticalPathKeys = criticalKeys,
                Utilization = builder.GetUtilization(),
                Stats = new GantStats
                {
                    TotalTasks = rawTasks.Count,
                    ScheduledTasks = scheduled.Count,
                    NotScheduled = rawTasks.Count - scheduled.Count,
                    TotalStoryPoints = scheduled.Sum(t => t.StoryPoints),
                    TotalWorkHours = Math.Round(scheduled.Sum(t => t.DurationWorkHours), 1),
                    CompletionDate = builder.CompletionDate,
                    TotalDurationHours = Math.Round(builder.TotalDurationHours, 1),
                    TotalDurationDays = Math.Round(builder.TotalDurationDays, 1),
                    CriticalPathCount = builder.CriticalPath.Count,
                    CriticalSp = builder.CriticalSp,
                }
            };
        }

        // ─── Jira REST ────────────────────────────────────────────

        private async Task<List<RawJiraTask>> FetchJiraTasksAsync(
    string server, string username, string password,
    List<string> projects, bool verifySsl, GantScheduleSettings settings,
    List<string> epicKeys)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = verifySsl
                    ? null
                    : (_, _, _, _) => true
            };

            using var client = new HttpClient(handler);
            client.BaseAddress = new Uri(server);
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            client.Timeout = TimeSpan.FromSeconds(120);

            var projectsStr = string.Join(", ", projects.Select(p => $"\"{p}\""));
            var jql = $"project in ({projectsStr}) AND status not in (\"Closed\", \"Done\", \"Resolved\")";

            // Фильтр по эпикам — берём только задачи, прикреплённые к выбранным эпикам.
            // В Jira Server поле зовётся "Epic Link"; в JQL так и пишется в кавычках.
            if (epicKeys != null && epicKeys.Count > 0)
            {
                var sanitized = epicKeys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => $"\"{k.Trim()}\"");
                var epicListStr = string.Join(", ", sanitized);
                if (!string.IsNullOrEmpty(epicListStr))
                    jql += $" AND \"Epic Link\" in ({epicListStr})";
            }

            var fields = "key,summary,status,priority,created,duedate,assignee," +
                         "issuelinks,customfield_10007,customfield_10372";

            var url = $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields={fields}&maxResults=200";

            _logger.LogInformation($"JQL запрос: {jql}");
            _logger.LogInformation($"URL: {url}");

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Jira API вернул {response.StatusCode}: {body}");
                throw new Exception($"Jira API вернул {response.StatusCode}: {body[..Math.Min(200, body.Length)]}");
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Получен JSON длиной {json.Length} символов");

            var result = JsonConvert.DeserializeObject<JiraSearchResult>(json)
                         ?? throw new Exception("Пустой ответ от Jira API");

            var tasks = new List<RawJiraTask>();

            _logger.LogInformation($"Найдено задач в Jira: {result.Issues?.Count ?? 0}");

            foreach (var issue in result.Issues ?? new())
            {
                var summary = issue.Fields?.Summary ?? "";
                var taskType = DetectTaskType(summary);

                _logger.LogDebug($"=== Обработка задачи {issue.Key} ===");
                _logger.LogDebug($"  Название: {summary}");
                _logger.LogDebug($"  Тип задачи: {taskType ?? "не определен"}");

                if (taskType == null)
                {
                    _logger.LogDebug($"  Пропущена: нет метки типа");
                    continue;
                }

                var sp = GetStoryPoints(issue.Fields);
                if (sp == null)
                {
                    _logger.LogDebug($"  Пропущена: нет Story Points");
                    continue;
                }
                _logger.LogDebug($"  Story Points: {sp}");

                _logger.LogDebug($"  Парсинг связей:");
                var links = ParseLinks(issue.Fields, _logger);

                _logger.LogDebug($"  Результат парсинга связей:");
                _logger.LogDebug($"    Блокирует: [{string.Join(",", links.Blocks)}]");
                _logger.LogDebug($"    Заблокирована: [{string.Join(",", links.BlockedBy)}]");

                tasks.Add(new RawJiraTask
                {
                    Key = issue.Key ?? "",
                    Summary = summary,
                    StoryPoints = sp.Value,
                    DurationWorkHours = settings.SpToWorkHoursConverted(sp.Value),
                    DurationWorkDays = settings.SpToWorkDays(sp.Value),
                    DurationTotalHours = settings.SpToTotalHoursConverted(sp.Value),
                    Assignee = issue.Fields?.Assignee?.DisplayName,
                    TaskType = taskType,
                    Priority = issue.Fields?.Priority?.Name ?? "Medium",
                    PriorityWeight = PriorityWeight(issue.Fields?.Priority?.Name),
                    CreatedDate = TryParseDate(issue.Fields?.Created) ?? DateTime.Now,
                    DueDate = TryParseDate(issue.Fields?.DueDate),
                    Link = $"{server}/browse/{issue.Key}",
                    Status = issue.Fields?.Status?.Name ?? "",
                    Blocks = links.Blocks,
                    BlockedBy = links.BlockedBy,
                });

                _logger.LogDebug($"  Задача добавлена");
            }

            _logger.LogInformation($"Отфильтровано задач: {tasks.Count}");
            return tasks;
        }

        private static string? DetectTaskType(string summary)
        {
            var u = summary.ToUpperInvariant();
            if (u.Contains("[TEST]")) return "TEST";
            if (u.Contains("[DEV BACK]")) return "DEV BACK";
            if (u.Contains("[DEV FRONT]")) return "DEV FRONT";
            if (u.Contains("[SA]")) return "SA";
            return null;
        }

        private static double? GetStoryPoints(JiraIssueFields? fields)
        {
            if (fields?.CustomField10372 == null) return null;

            try
            {
                if (fields.CustomField10372 is double d && d > 0)
                    return d;
                if (fields.CustomField10372 is string s && double.TryParse(s, out var v) && v > 0)
                    return v;
                if (fields.CustomField10372 is long l && l > 0)
                    return l;
                if (fields.CustomField10372 is int i && i > 0)
                    return i;
            }
            catch (Exception ex)
            {
                // Логируем ошибку
                Console.WriteLine($"Error parsing SP: {ex.Message}");
            }
            return null;
        }

        private static (List<string> Blocks, List<string> BlockedBy) ParseLinks(JiraIssueFields? fields, ILogger logger)
        {
            var blocks = new List<string>();
            var blockedBy = new List<string>();

            if (fields?.IssueLinks == null)
            {
                logger?.LogDebug("    Нет связей");
                return (blocks, blockedBy);
            }

            logger?.LogDebug($"    Всего связей: {fields.IssueLinks.Count}");

            foreach (var link in fields.IssueLinks)
            {
                var typeName = link.Type?.Name?.ToLowerInvariant() ?? "unknown";
                var outwardKey = link.OutwardIssue?.Key ?? "none";
                var inwardKey = link.InwardIssue?.Key ?? "none";

                logger?.LogDebug($"    Связь: тип='{typeName}', outward={outwardKey}, inward={inwardKey}");

                if (typeName.Contains("block"))
                {
                    if (link.OutwardIssue?.Key != null)
                    {
                        blocks.Add(link.OutwardIssue.Key);
                        logger?.LogDebug($"      -> {fields.Summary} БЛОКИРУЕТ {link.OutwardIssue.Key}");
                    }
                    if (link.InwardIssue?.Key != null)
                    {
                        blockedBy.Add(link.InwardIssue.Key);
                        logger?.LogDebug($"      -> {fields.Summary} ЗАБЛОКИРОВАНА {link.InwardIssue.Key}");
                    }
                }
                else if (typeName.Contains("depends") || typeName.Contains("relates"))
                {
                    if (link.OutwardIssue?.Key != null)
                    {
                        blockedBy.Add(link.OutwardIssue.Key);
                        logger?.LogDebug($"      -> {fields.Summary} зависит от {link.OutwardIssue.Key}");
                    }
                    if (link.InwardIssue?.Key != null)
                    {
                        blocks.Add(link.InwardIssue.Key);
                        logger?.LogDebug($"      -> {link.InwardIssue.Key} зависит от {fields.Summary}");
                    }
                }
                else
                {
                    logger?.LogDebug($"      -> Неизвестный тип связи: {typeName}");
                }
            }

            logger?.LogDebug($"    ИТОГ: блокирует=[{string.Join(",", blocks)}], заблокирована=[{string.Join(",", blockedBy)}]");

            return (blocks, blockedBy);
        }
        private static int PriorityWeight(string? p) => p switch
        {
            "Highest" => 1,
            "High" => 2,
            "Medium" => 3,
            "Low" => 4,
            "Lowest" => 5,
            _ => 3
        };

        private static DateTime? TryParseDate(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return DateTime.TryParse(s[..Math.Min(10, s.Length)], out var d) ? d : null;
        }

        // ─── Helpers ──────────────────────────────────────────────

        private static GantRole? ParseRole(string role) => role.ToLowerInvariant() switch
        {
            "tester" => GantRole.Tester,
            "analyst" => GantRole.Analyst,
            "backend_dev" => GantRole.BackendDev,
            "frontend_dev" => GantRole.FrontendDev,
            _ => null
        };

        private static TimeOnly ParseTime(string t)
        {
            var parts = t.Split(':');
            return parts.Length == 2
                ? new TimeOnly(int.Parse(parts[0]), int.Parse(parts[1]))
                : new TimeOnly(9, 0);
        }

        private static GantGenerateResponse Fail(string error) =>
            new() { Success = false, Error = error };

        // ─── Jira API response DTOs ───────────────────────────────

        private class JiraSearchResult
        {
            [JsonProperty("issues")]
            public List<JiraIssue>? Issues { get; set; }
        }

        private class JiraIssue
        {
            [JsonProperty("key")]
            public string? Key { get; set; }

            [JsonProperty("fields")]
            public JiraIssueFields? Fields { get; set; }
        }

        private class JiraIssueFields
        {
            [JsonProperty("summary")]
            public string? Summary { get; set; }

            [JsonProperty("created")]
            public string? Created { get; set; }

            [JsonProperty("duedate")]
            public string? DueDate { get; set; }

            [JsonProperty("customfield_10372")]
            public object? CustomField10372 { get; set; }

            [JsonProperty("status")]
            public JiraNamedObject? Status { get; set; }

            [JsonProperty("priority")]
            public JiraNamedObject? Priority { get; set; }

            [JsonProperty("assignee")]
            public JiraAssignee? Assignee { get; set; }

            [JsonProperty("issuelinks")]
            public List<JiraIssueLink>? IssueLinks { get; set; }
        }

        private class JiraNamedObject
        {
            [JsonProperty("name")]
            public string? Name { get; set; }
        }

        private class JiraAssignee
        {
            [JsonProperty("displayName")]
            public string? DisplayName { get; set; }
        }

        private class JiraIssueLink
        {
            [JsonProperty("type")]
            public JiraNamedObject? Type { get; set; }

            [JsonProperty("outwardIssue")]
            public JiraLinkedIssue? OutwardIssue { get; set; }

            [JsonProperty("inwardIssue")]
            public JiraLinkedIssue? InwardIssue { get; set; }
        }

        private class JiraLinkedIssue
        {
            [JsonProperty("key")]
            public string? Key { get; set; }
        }
    }
}