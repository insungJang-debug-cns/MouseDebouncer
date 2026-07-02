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
    private ToolStripMenuItem _menuLog     = null!;

    // Application.Run이 내부적으로 Show()를 호출하므로 최초 1회만 억제
    // 단, 설정 파일이 없는 최초 실행 시에는 화면을 띄워준다.
    private bool _suppressFirstShow;

    public MainForm()
    {
        _suppressFirstShow = ConfigManager.ConfigExists;
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
        Text            = "더블클릭 방지";
        ClientSize      = new Size(310, 295);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 9,
            Padding     = new Padding(10),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        for (int i = 0; i < 9; i++)
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

        AddRow("사이드 버튼 1 딜레이 (ms):", _nudXButton1);
        AddRow("사이드 버튼 2 딜레이 (ms):", _nudXButton2);
        AddRow("왼쪽 버튼 딜레이 (ms):",    _nudLeft);
        AddRow("오른쪽 버튼 딜레이 (ms):",   _nudRight);
        AddRow("휠 클릭 딜레이 (ms):",      _nudMiddle);

        var lblHint = new Label
        {
            Text      = "※ 단위: ms  (1000 = 1초)",
            ForeColor = SystemColors.GrayText,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(lblHint, 0, row);
        layout.SetColumnSpan(lblHint, 2);
        row++;

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
            Text      = "더블클릭 방지횟수: 0",
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(_lblBlocked, 0, row);

        var btnSave = new Button { Text = "저장", Dock = DockStyle.Fill };
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
        _menuToggle  = new ToolStripMenuItem("비활성화", null, OnToggle);
        _menuBlocked = new ToolStripMenuItem("오늘 방지횟수: 0") { Enabled = false };
        _menuLog     = new ToolStripMenuItem("로그 기록 시작", null, OnToggleLog);

        var menu = new ContextMenuStrip();
        menu.Opening += OnTrayMenuOpening;
        menu.Items.Add(new ToolStripMenuItem("설정 열기", null, (_, _) => OpenSettings()));
        menu.Items.Add(_menuToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_menuBlocked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_menuLog);
        menu.Items.Add(new ToolStripMenuItem("로그 파일 열기", null, OnOpenLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("종료", null, OnExit));

        _trayIcon = new NotifyIcon
        {
            Icon             = SystemIcons.Shield,
            ContextMenuStrip = menu,
            Visible          = true,
            Text             = "더블클릭 방지",
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
    }

    // -------------------------------------------------------------------------
    // 이벤트 핸들러
    // -------------------------------------------------------------------------

    private void OnTrayMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _menuBlocked.Text = $"오늘 방지횟수: {_debounceManager.BlockedCount}";
        _menuToggle.Text  = _mouseHook.IsEnabled ? "비활성화" : "활성화";
    }

    private void OpenSettings()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _lblBlocked.Text = $"더블클릭 방지횟수: {_debounceManager.BlockedCount}";
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
            ? "더블클릭 방지"
            : "더블클릭 방지 (비활성)";
    }

    private void OnToggleLog(object? sender, EventArgs e)
    {
        Logger.IsEnabled = !Logger.IsEnabled;
        _menuLog.Text = Logger.IsEnabled ? "로그 기록 중지" : "로그 기록 시작";
        if (Logger.IsEnabled) Logger.Clear();
    }

    private void OnOpenLog(object? sender, EventArgs e)
    {
        if (!File.Exists(Logger.LogFilePath))
        {
            MessageBox.Show("로그 파일이 없습니다.\n먼저 로그 기록을 시작하세요.", "더블클릭 방지",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        System.Diagnostics.Process.Start("notepad.exe", Logger.LogFilePath);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void OnBlockCountChanged(object? sender, EventArgs e)
    {
        if (Visible && IsHandleCreated)
            _lblBlocked.Text = $"더블클릭 방지횟수: {_debounceManager.BlockedCount}";
    }

    private void OnButtonDetected(object? sender, MouseButton button)
    {
        if (!Visible || !IsHandleCreated) return;
        _lblLastButton.Text = button switch
        {
            MouseButton.Left     => "왼쪽 버튼",
            MouseButton.Right    => "오른쪽 버튼",
            MouseButton.Middle   => "휠 클릭",
            MouseButton.XButton1 => "사이드 버튼 1",
            MouseButton.XButton2 => "사이드 버튼 2",
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
