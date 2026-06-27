using Microsoft.Win32;

namespace MouseDebouncer;

/// <summary>
/// 설정 UI + 시스템 트레이 아이콘을 담당하는 메인 폼.
/// 닫기(X) 버튼 → Hide() 처리하여 트레이에서 계속 실행.
/// </summary>
public class MainForm : Form
{
    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MouseDebouncer";

    private readonly DebounceManager _debounceManager;
    private readonly MouseHook       _mouseHook;
    private AppConfig _config;

    // --- UI Controls ---
    private NumericUpDown _nudXButton1 = null!;
    private NumericUpDown _nudXButton2 = null!;
    private NumericUpDown _nudLeft     = null!;
    private NumericUpDown _nudRight    = null!;
    private NumericUpDown _nudMiddle   = null!;
    private CheckBox      _chkStartup     = null!;
    private Label         _lblBlocked     = null!;
    private Label         _lblLastButton  = null!;

    // --- Tray ---
    private NotifyIcon        _trayIcon    = null!;
    private ToolStripMenuItem _menuToggle  = null!;
    private ToolStripMenuItem _menuBlocked = null!;

    // Application.Run이 내부적으로 Show()를 호출하므로 최초 1회만 억제
    private bool _suppressFirstShow = true;

    public MainForm()
    {
        _config          = ConfigManager.Load();
        _debounceManager = new DebounceManager(_config);
        _mouseHook       = new MouseHook(_debounceManager);

        BuildUI();
        BuildTray();

        _mouseHook.Install();

        // Hook 콜백은 UI 스레드에서 실행되므로 BeginInvoke 불필요
        _debounceManager.BlockCountChanged += OnBlockCountChanged;
        _mouseHook.ButtonDetected          += OnButtonDetected;
    }

    // -------------------------------------------------------------------------
    // UI 구성
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        Text            = "Mouse Debouncer";
        ClientSize      = new Size(310, 270);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 8,
            Padding     = new Padding(10),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        for (int i = 0; i < 8; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _nudXButton1 = CreateNud(_config.XButton1);
        _nudXButton2 = CreateNud(_config.XButton2);
        _nudLeft     = CreateNud(_config.Left);
        _nudRight    = CreateNud(_config.Right);
        _nudMiddle   = CreateNud(_config.Middle);

        int row = 0;

        void AddRow(string labelText, Control ctrl)
        {
            layout.Controls.Add(
                new Label { Text = labelText, Anchor = AnchorStyles.Left | AnchorStyles.Right,
                            TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(ctrl, 1, row);
            row++;
        }

        AddRow("XButton1 Delay (ms):", _nudXButton1);
        AddRow("XButton2 Delay (ms):", _nudXButton2);
        AddRow("Left Delay (ms):",     _nudLeft);
        AddRow("Right Delay (ms):",    _nudRight);
        AddRow("Middle Delay (ms):",   _nudMiddle);

        _chkStartup = new CheckBox
        {
            Text    = "시작 프로그램 등록",
            Checked = IsStartupEnabled(),
            Anchor  = AnchorStyles.Left | AnchorStyles.Right,
        };
        layout.Controls.Add(_chkStartup, 0, row);
        layout.SetColumnSpan(_chkStartup, 2);
        row++;

        layout.Controls.Add(
            new Label { Text = "누른 버튼:", Anchor = AnchorStyles.Left | AnchorStyles.Right,
                        TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _lblLastButton = new Label
        {
            Text      = "—",
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            Font      = new Font(Font, FontStyle.Bold),
        };
        layout.Controls.Add(_lblLastButton, 1, row);
        row++;

        _lblBlocked = new Label
        {
            Text      = "Blocked: 0",
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(_lblBlocked, 0, row);

        var btnSave = new Button { Text = "Save", Dock = DockStyle.Fill };
        btnSave.Click += OnSave;
        layout.Controls.Add(btnSave, 1, row);

        Controls.Add(layout);
    }

    private static NumericUpDown CreateNud(int value) => new()
    {
        Minimum = 0,
        Maximum = 5000,
        Value   = Math.Clamp(value, 0, 5000),
        Anchor  = AnchorStyles.Left,
    };

    // -------------------------------------------------------------------------
    // 시스템 트레이 구성
    // -------------------------------------------------------------------------

    private void BuildTray()
    {
        _menuToggle  = new ToolStripMenuItem("Disable", null, OnToggle);
        _menuBlocked = new ToolStripMenuItem("Blocked today: 0") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Opening += OnTrayMenuOpening;
        menu.Items.Add(new ToolStripMenuItem("Open Settings", null, (_, _) => OpenSettings()));
        menu.Items.Add(_menuToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_menuBlocked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        _trayIcon = new NotifyIcon
        {
            Icon             = SystemIcons.Shield,
            ContextMenuStrip = menu,
            Visible          = true,
            Text             = "Mouse Debouncer",
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
    }

    // -------------------------------------------------------------------------
    // 이벤트 핸들러
    // -------------------------------------------------------------------------

    private void OnTrayMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _menuBlocked.Text = $"Blocked today: {_debounceManager.BlockedCount}";
        _menuToggle.Text  = _mouseHook.IsEnabled ? "Disable" : "Enable";
    }

    private void OpenSettings()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _lblBlocked.Text = $"Blocked: {_debounceManager.BlockedCount}";
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _config.XButton1 = (int)_nudXButton1.Value;
        _config.XButton2 = (int)_nudXButton2.Value;
        _config.Left     = (int)_nudLeft.Value;
        _config.Right    = (int)_nudRight.Value;
        _config.Middle   = (int)_nudMiddle.Value;

        ConfigManager.Save(_config);
        _debounceManager.UpdateConfig(_config);
        SetStartup(_chkStartup.Checked);
    }

    private void OnToggle(object? sender, EventArgs e)
    {
        _mouseHook.IsEnabled = !_mouseHook.IsEnabled;
        _trayIcon.Icon = _mouseHook.IsEnabled ? SystemIcons.Shield : SystemIcons.Warning;
        _trayIcon.Text = _mouseHook.IsEnabled
            ? "Mouse Debouncer"
            : "Mouse Debouncer (Disabled)";
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void OnBlockCountChanged(object? sender, EventArgs e)
    {
        if (Visible && IsHandleCreated)
            _lblBlocked.Text = $"Blocked: {_debounceManager.BlockedCount}";
    }

    private void OnButtonDetected(object? sender, MouseButton button)
    {
        if (!Visible || !IsHandleCreated) return;
        _lblLastButton.Text = button switch
        {
            MouseButton.Left     => "Left (왼쪽)",
            MouseButton.Right    => "Right (오른쪽)",
            MouseButton.Middle   => "Middle (휠 클릭)",
            MouseButton.XButton1 => "XButton1 (사이드 버튼 1)",
            MouseButton.XButton2 => "XButton2 (사이드 버튼 2)",
            _                    => button.ToString()
        };
    }

    // -------------------------------------------------------------------------
    // 시작 프로그램 등록 (HKCU\...\Run)
    // -------------------------------------------------------------------------

    private bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) != null;
    }

    private void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enable)
            key.SetValue(AppName, Application.ExecutablePath);
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }

    // -------------------------------------------------------------------------
    // Form 오버라이드
    // -------------------------------------------------------------------------

    /// <summary>
    /// Application.Run 내부의 Show() 호출을 가로채 최초 표시를 억제한다.
    /// 핸들은 생성해야 NotifyIcon 등 초기화가 정상 동작한다.
    /// </summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_suppressFirstShow)
        {
            _suppressFirstShow = false;
            if (!IsHandleCreated) CreateHandle();
            return;
        }
        base.SetVisibleCore(value);
    }

    /// <summary>
    /// 사용자가 X 버튼을 누르면 종료하지 않고 트레이로 숨긴다.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mouseHook.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
