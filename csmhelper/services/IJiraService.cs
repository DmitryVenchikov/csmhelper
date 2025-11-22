using csmhelper.Models;

namespace csmhelper.services
{
    public interface IJiraService
    {
        Task<bool> AuthenticateAsync(string username, string password);
        Task<bool> IsAuthenticatedAsync();
        Task LogoutAsync();
        Task<TaskCreationResponse> CreateLinkedTasksAsync(TaskCreationModel model);
    }
}
