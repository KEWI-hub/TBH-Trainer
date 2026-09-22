#pragma warning disable CA1416

namespace TBH_Trainer;

/// <summary>
/// Flat button whose DISABLED state stays readable on a dark theme.
/// Default WinForms grays disabled text to near-black, which vanishes on a
/// dark background — here we owner-draw the disabled look with a legible gray.
/// When enabled it defers to the normal flat rendering.
/// </summary>
internal sealed class ThemedButton : Button
{
    private static readonly Color DisabledFore = Color.FromArgb(140, 146, 162);
    private static readonly Color DisabledBack = Color.FromArgb(24, 26, 34);
    private static readonly Color DisabledBorder = Color.FromArgb(58, 63, 76);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        g.Clear(DisabledBack);
        using (var pen = new Pen(DisabledBorder))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(
            g, Text, Font, ClientRectangle, DisabledFore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}
