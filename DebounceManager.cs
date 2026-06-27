namespace MouseDebouncer;

/// <summary>
/// 버튼별 마지막 입력 시간을 기억하고, 딜레이 이내 재입력을 차단할지 판단한다.
/// </summary>
public class DebounceManager
{
    private readonly Dictionary<MouseButton, DateTime> _lastInputTime = new();
    private AppConfig _config;

    // 오늘 날짜가 바뀌면 카운트를 리셋한다
    private DateTime _countDate = DateTime.Today;
    private int _blockedCount = 0;

    /// <summary>
    /// 차단 카운트가 증가할 때 발생. Hook 콜백(UI 스레드)에서 발생하므로 별도 마샬링 불필요.
    /// </summary>
    public event EventHandler? BlockCountChanged;

    public int BlockedCount
    {
        get
        {
            ResetIfNewDay();
            return _blockedCount;
        }
    }

    public DebounceManager(AppConfig config)
    {
        _config = config;
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 해당 버튼 입력을 차단해야 하면 true를 반환하고 카운트를 증가시킨다.
    /// 허용하면 false를 반환하고 마지막 입력 시간을 갱신한다.
    /// </summary>
    public bool ShouldBlock(MouseButton button)
    {
        int delayMs = GetDelay(button);
        if (delayMs <= 0)
            return false;

        DateTime now = DateTime.UtcNow;

        if (_lastInputTime.TryGetValue(button, out DateTime lastTime))
        {
            if ((now - lastTime).TotalMilliseconds < delayMs)
            {
                ResetIfNewDay();
                _blockedCount++;
                BlockCountChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }

        _lastInputTime[button] = now;
        return false;
    }

    private int GetDelay(MouseButton button) => button switch
    {
        MouseButton.Left     => _config.Left,
        MouseButton.Right    => _config.Right,
        MouseButton.Middle   => _config.Middle,
        MouseButton.XButton1 => _config.XButton1,
        MouseButton.XButton2 => _config.XButton2,
        _                    => 0
    };

    private void ResetIfNewDay()
    {
        if (DateTime.Today > _countDate)
        {
            _countDate = DateTime.Today;
            _blockedCount = 0;
        }
    }
}
