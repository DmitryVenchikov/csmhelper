using csmhelper.Models;

namespace csmhelper.services
{
    public interface IGanttService
    {
        Task<GanttData> GetDataAsync();

        // Team members
        Task<TeamMember> AddMemberAsync(TeamMember member);
        Task<bool> UpdateMemberAsync(TeamMember member);
        Task<bool> DeleteMemberAsync(string id);

        // Vacations
        Task<VacationPeriod> AddVacationAsync(VacationPeriod vacation);
        Task<bool> UpdateVacationAsync(VacationPeriod vacation);
        Task<bool> DeleteVacationAsync(string id);

        // Tasks
        Task<GanttTask> AddTaskAsync(GanttTask task);
        Task<bool> UpdateTaskAsync(GanttTask task);
        Task<bool> DeleteTaskAsync(string id);

        // Business logic: compute effective end date skipping vacation days
        EffectiveDateResult ComputeEffectiveEnd(string memberId, DateTime start, int workDays, GanttData data);
    }
}
