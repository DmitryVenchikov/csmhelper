namespace csmhelper.Models
{
    public class TeamMember
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#4facfe";
        public string Role { get; set; } = string.Empty;
    }

    public class VacationPeriod
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string MemberId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class GanttTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string MemberId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Type { get; set; } = "task"; // task | milestone | sprint
        public string Color { get; set; } = "#28a745";
        public string Description { get; set; } = string.Empty;
        public int Progress { get; set; } = 0; // 0-100
    }

    public class GanttData
    {
        public List<TeamMember> Members { get; set; } = new();
        public List<VacationPeriod> Vacations { get; set; } = new();
        public List<GanttTask> Tasks { get; set; } = new();
    }

    // DTO для расчёта эффективных дат задачи с учётом отпусков
    public class EffectiveDateResult
    {
        public DateTime OriginalEnd { get; set; }
        public DateTime EffectiveEnd { get; set; }
        public int VacationDaysOverlap { get; set; }
        public List<VacationPeriod> OverlappingVacations { get; set; } = new();
    }
}
