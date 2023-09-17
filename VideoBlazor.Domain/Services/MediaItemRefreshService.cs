
namespace VideoBlazor.Domain.Services;

public class MediaItemRefreshService
{
    public event Func<Task> OnRefreshRequested;

    public void RequestRefresh()
    {
        OnRefreshRequested?.Invoke();
    }
}