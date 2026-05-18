using csmhelper.Controllers;
using csmhelper.Models;

namespace csmhelper.services
{
    public interface IJiraService
    {
        Task<bool> AuthenticateAsync(string username, string password);
        Task<bool> IsAuthenticatedAsync();
        Task LogoutAsync();
        Task<TaskCreationResponse> CreateLinkedTasksAsync(TaskCreationModel model);
        Task<List<ProcessResult>> ProcessTasksAsync(List<string> taskKeys, string actionType, string targetStatus, string resolution);
        Task<List<JiraTask>> SearchTasksAsync(CleanupFilterModel filters);
        Task<JiraEpicsResponse> GetEpicsByProjectAsync(string projectKey);
    }
}
