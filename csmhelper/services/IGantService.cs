using csmhelper.Models;

namespace csmhelper.services
{
    public interface IGantService
    {
        Task<GantGenerateResponse> GenerateAsync(GantGenerateRequest request);
    }
}
