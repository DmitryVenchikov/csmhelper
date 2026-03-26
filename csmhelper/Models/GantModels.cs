using System.ComponentModel.DataAnnotations;

namespace csmhelper.Models
{
    // ─── Request ──────────────────────────────────────────────────

    public class GantGenerateRequest
    {
        [Required]
        public string JiraServer { get; set; } = "https://jira.moscow.alfaintra.net";

        [Required]
        public List<string> Projects { get; set; } = new();

        public bool VerifySsl { get; set; } = false;

        [Required]
        public List<GantEmployeeInput> Employees { get; set; } = new();

        public GantScheduleSettings Schedule { get; set; } = new();
    }

    public class GantEmployeeInput
    {
        [Required]
        public string Name { get; set; }

        /// <summary>tester | analyst | backend_dev | frontend_dev</summary>
        [Required]
        public string Role { get; set; }

        public int HoursPerDay { get; set; } = 8;
        public string WorkStart { get; set; } = "09:00";
        public string WorkEnd { get; set; } = "18:00";
        public string LunchStart { get; set; } = "13:00";
        public string LunchEnd { get; set; } = "14:00";
    }

    public class GantScheduleSettings
    {
        public int SpBase { get; set; } = 21;
        public int SprintHoursTotal { get; set; } = 80;
        public double AgilePercent { get; set; } = 15;
        public int TaskTransitionLagMinutes { get; set; } = 60;

        public double SpToWorkHours => SprintHoursTotal * (1 - AgilePercent / 100) / SpBase;
        public double SpToTotalHours => (double)SprintHoursTotal / SpBase;

        public double SpToWorkDays(double sp) => sp / SpBase * 10;
        public double SpToWorkHoursConverted(double sp) => sp * SpToWorkHours;
        public double SpToTotalHoursConverted(double sp) => sp * SpToTotalHours;
    }

    // ─── Response ─────────────────────────────────────────────────

    public class GantGenerateResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public GantStats? Stats { get; set; }
        public List<GantScheduledTask> Scheduled { get; set; } = new();
        public List<string> CriticalPathKeys { get; set; } = new();
        public Dictionary<string, double> Utilization { get; set; } = new();
        public string? GanttImageBase64 { get; set; }  // PNG rendered server-side (optional)
    }

    public class GantStats
    {
        public int TotalTasks { get; set; }
        public int ScheduledTasks { get; set; }
        public int NotScheduled { get; set; }
        public double TotalStoryPoints { get; set; }
        public double TotalWorkHours { get; set; }
        public DateTime? CompletionDate { get; set; }
        public double? TotalDurationHours { get; set; }
        public double? TotalDurationDays { get; set; }
        public int CriticalPathCount { get; set; }
        public double CriticalSp { get; set; }
    }

    public class GantScheduledTask
    {
        public string Key { get; set; } = "";
        public string Summary { get; set; } = "";
        public double StoryPoints { get; set; }
        public double DurationWorkHours { get; set; }
        public double DurationWorkDays { get; set; }
        public string TaskType { get; set; } = "";
        public string? AssignedResource { get; set; }
        public string? AssignmentReason { get; set; }
        public DateTime ScheduledStart { get; set; }
        public DateTime ScheduledEnd { get; set; }
        public string Priority { get; set; } = "Medium";
        public DateTime? DueDate { get; set; }
        public string Link { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsCritical { get; set; }
        public List<string> BlockedBy { get; set; } = new();
        public List<string> Blocks { get; set; } = new();
    }

    // ─── Internal domain objects ──────────────────────────────────

    public enum GantRole { Tester, Analyst, BackendDev, FrontendDev }

    public class GantEmployee
    {
        public string Name { get; set; } = "";
        public GantRole Role { get; set; }
        public int HoursPerDay { get; set; } = 8;
        public TimeOnly WorkStart { get; set; } = new(9, 0);
        public TimeOnly WorkEnd { get; set; } = new(18, 0);
        public TimeOnly LunchStart { get; set; } = new(13, 0);
        public TimeOnly LunchEnd { get; set; } = new(14, 0);

        public double WorkHoursPerDay =>
            (WorkEnd.Hour - WorkStart.Hour) - (LunchEnd.Hour - LunchStart.Hour);

        public bool CanHandle(string taskType) => taskType switch
        {
            "TEST" => Role == GantRole.Tester,
            "DEV BACK" => Role == GantRole.BackendDev,
            "DEV FRONT" => Role == GantRole.FrontendDev,
            "SA" => Role == GantRole.Analyst,
            _ => false
        };
    }

    internal class RawJiraTask
    {
        public string Key { get; set; } = "";
        public string Summary { get; set; } = "";
        public double StoryPoints { get; set; }
        public double DurationWorkHours { get; set; }
        public double DurationWorkDays { get; set; }
        public double DurationTotalHours { get; set; }
        public string? Assignee { get; set; }
        public string TaskType { get; set; } = "";
        public string Sprint { get; set; } = "";
        public string Priority { get; set; } = "Medium";
        public int PriorityWeight { get; set; } = 3;
        public DateTime CreatedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Link { get; set; } = "";
        public string Status { get; set; } = "";
        public List<string> Blocks { get; set; } = new();
        public List<string> BlockedBy { get; set; } = new();
    }
}
