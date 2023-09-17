
namespace VideoBlazor.Core.Utils;
public class Debouncer
{
    private bool _isDebouncing = false;
    private readonly int _debounceTime;

    public Debouncer(int debounceTimeInMilliseconds)
    {
        _debounceTime = debounceTimeInMilliseconds;
    }

    public async Task Debounce(Func<Task> action)
    {
        if (_isDebouncing) return;

        _isDebouncing = true;

        await action();  // Execute the function

        await Task.Delay(_debounceTime);

        _isDebouncing = false;
    }
}
