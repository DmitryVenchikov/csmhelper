namespace csmhelper.Models
{
    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }

    public class TaskCreationResponse : ApiResponse
    {
        public int TasksCreated { get; set; }
        public int LinksCreated { get; set; }
        public List<CreatedTask> Tasks { get; set; } = new List<CreatedTask>();
    }

    public class CreatedTask
    {
        public string Key { get; set; }
        public string Summary { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }
    }

    public class AuthResponse : ApiResponse
    {
        public bool Authenticated { get; set; }
    }
}
