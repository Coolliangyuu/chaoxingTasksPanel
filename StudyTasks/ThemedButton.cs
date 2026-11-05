using System.Drawing.Drawing2D;

namespace StudyTasks;

// Retains Button's sizing, keyboard and accessibility behavior. Only drawing changes.
internal sealed class ThemedButton : Button
{
    private bool hovered;
    private bool pressed;
    private readonly PaintEventHandler render;

    internal ThemedButton()
    {
        DoubleBuffered = true;
        render = Render;
        Paint += render;
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }

    private void Render(object? sender, PaintEventArgs e)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        bool primary = FlatAppearance.BorderSize == 0;
        Color fill = !Enabled ? Color.FromArgb(236, 241, 244) :
            primary ? (pressed ? Color.FromArgb(8, 86, 100) : hovered ? Color.FromArgb(10, 99, 114) : BackColor) :
            pressed ? Color.FromArgb(225, 239, 242) : hovered ? Color.FromArgb(240, 247, 249) : BackColor;
        Color foreground = !Enabled ? Color.FromArgb(137, 151, 162) : ForeColor;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(new RectangleF(.6f, .6f, Width - 1.2f, Height - 1.2f), 6f * DeviceDpi / 96f);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        if (!primary)
        {
            using var pen = new Pen(hovered ? Color.FromArgb(132, 181, 192) : Color.FromArgb(218, 227, 234));
            g.DrawPath(pen, shape);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, foreground, flags);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(primary ? Color.FromArgb(209, 240, 241) : Color.FromArgb(13, 113, 129)) { DashStyle = DashStyle.Dot };
            using var focus = Rounded(RectangleF.Inflate(ClientRectangle, -3.5f, -3.5f), 4f * DeviceDpi / 96f);
            g.DrawPath(pen, focus);
        }
    }
}
