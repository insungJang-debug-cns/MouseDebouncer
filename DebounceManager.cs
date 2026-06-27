namespace MouseDebouncer;

/// <summary>
/// 버튼별 마지막 입력 시간을 기억하고, 딜레이 이내 재입력을 차단할지 판단한다.
/// </summary>
public class DebounceManager
{
    private readonly Dictionary<MouseButton, DateTime> _lastInputTime = new();
    private readonly HashSet<MouseButton> _pendingBlockUp = new();
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
            double elapsed = (now - lastTime).TotalMilliseconds;
            if (elapsed < delayMs)
            {
                Logger.Write($"{ButtonName(button)} | 차단  | 경과: {elapsed,6:F0}ms | 딜레이: {delayMs}ms");
                _pendingBlockUp.Add(button);
                ResetIfNewDay();
                _blockedCount++;
                BlockCountChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            Logger.Write($"{ButtonName(button)} | 통과  | 경과: {elapsed,6:F0}ms | 딜레이: {delayMs}ms");
        }
        else
        {
            Logger.Write($"{ButtonName(button)} | 통과  | 경과: 첫 입력");
        }

        _pendingBlockUp.Remove(button);
        _lastInputTime[button] = now;
        return false;
    }

    /// DOWN이 차단된 버튼의 UP도 차단한다.
    public bool ShouldBlockUp(MouseButton button) => _pendingBlockUp.Remove(button);

    private static string ButtonName(MouseButton button) => button switch
    {
        MouseButton.Left     => "왼쪽 버튼",
        MouseButton.Right    => "오른쪽 버튼",
        MouseButton.Middle   => "휠 클릭",
        MouseButton.XButton1 => "사이드 버튼 1",
        MouseButton.XButton2 => "사이드 버튼 2",
        _                    => button.ToString()
    };

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
