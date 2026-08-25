namespace OwlUsageTray;

internal sealed class LoginForm : Form
{
    private readonly TextBox _emailBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _loginButton = new();
    private readonly OwlApiClient _apiClient;

    public LoginForm(OwlApiClient apiClient)
    {
        _apiClient = apiClient;
        Text = "OwlAI 用量登录";
        ClientSize = new Size(380, 278);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(18, 24, 38);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "OwlAI 实时用量",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(28, 22)
        };

        var hint = new Label
        {
            Text = "首次启动登录一次，密码不会保存。",
            ForeColor = Color.FromArgb(154, 164, 180),
            AutoSize = true,
            Location = new Point(30, 57)
        };

        ConfigureTextBox(_emailBox, new Point(30, 91), "邮箱");
        ConfigureTextBox(_passwordBox, new Point(30, 137), "密码");
        _passwordBox.UseSystemPasswordChar = true;

        _loginButton.Text = "登录并开始监控";
        _loginButton.Location = new Point(30, 190);
        _loginButton.Size = new Size(320, 38);
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.BackColor = Color.FromArgb(59, 130, 246);
        _loginButton.ForeColor = Color.White;
        _loginButton.Cursor = Cursors.Hand;
        _loginButton.Click += async (_, _) => await LoginAsync();

        _statusLabel.Location = new Point(30, 236);
        _statusLabel.Size = new Size(320, 30);
        _statusLabel.ForeColor = Color.FromArgb(248, 113, 113);
        _statusLabel.TextAlign = ContentAlignment.TopCenter;

        Controls.AddRange([title, hint, _emailBox, _passwordBox, _loginButton, _statusLabel]);
        AcceptButton = _loginButton;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _emailBox.Focus();
    }

    private static void ConfigureTextBox(TextBox textBox, Point location, string placeholder)
    {
        textBox.Location = location;
        textBox.Size = new Size(320, 34);
        textBox.PlaceholderText = placeholder;
        textBox.BackColor = Color.FromArgb(30, 41, 59);
        textBox.ForeColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Microsoft YaHei UI", 10F);
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_emailBox.Text) || string.IsNullOrEmpty(_passwordBox.Text))
        {
            _statusLabel.Text = "请输入邮箱和密码。";
            return;
        }

        SetBusy(true);
        try
        {
            await _apiClient.LoginAsync(_emailBox.Text.Trim(), _passwordBox.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _statusLabel.Text = FriendlyError(exception);
        }
        finally
        {
            _passwordBox.Clear();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _emailBox.Enabled = !busy;
        _passwordBox.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _loginButton.Text = busy ? "正在登录…" : "登录并开始监控";
        if (busy) _statusLabel.Text = "";
    }

    private static string FriendlyError(Exception exception)
    {
        if (exception is HttpRequestException http && http.StatusCode is not null)
        {
            return http.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "邮箱或密码不正确。"
                : $"服务器返回错误：{(int)http.StatusCode}";
        }

        return exception.Message.Length > 80
            ? "登录失败，请检查网络或账号信息。"
            : exception.Message;
    }
}
