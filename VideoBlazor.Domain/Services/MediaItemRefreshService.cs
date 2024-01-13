
namespace VideoBlazor.Domain.Services;

public class MediaItemRefreshService
{
    public event Func<Guid, Task> OnRefreshRequested;

    public void RequestRefresh(Guid itemId)
    {
        OnRefreshRequested?.Invoke(itemId);
    }
}