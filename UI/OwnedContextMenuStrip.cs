using System.Windows.Forms;
using HASNetMonitor.Interop;

namespace HASNetMonitor.UI;

/// <summary>
/// A context menu owned by the widget's window.
/// </summary>
/// <remarks>
/// A <see cref="ContextMenuStrip"/> shown with no owner is a top-level popup. When the
/// app then forces it to the foreground, Windows - which refuses a foreground steal from
/// a process that is not already foreground - answers by flashing the process's taskbar
/// button: the app icon that appeared and went away while a menu item was dragged.
/// <para>
/// Owning the popup by the widget fixes both halves. An owned window is composited with
/// its owner and never gets a taskbar button of its own, and because the owner lives in
/// this process the drop-down still closes on an outside click through WinForms' own
/// message filter, so no foreground nudge is needed.
/// </para>
/// </remarks>
internal sealed class OwnedContextMenuStrip : ContextMenuStrip
{
    private readonly IntPtr _owner;

    public OwnedContextMenuStrip(IntPtr owner)
    {
        _owner = owner;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;

            // GWLP_HWNDPARENT is the owner of a top-level window. Setting it here, as the
            // handle is created, is what makes the popup owned rather than free-standing.
            if (_owner != IntPtr.Zero)
            {
                cp.Parent = _owner;
            }

            // A popup without WS_EX_TOOLWINDOW is a candidate for a taskbar button, and
            // the shell flashes that button when the popup is raised over the taskbar -
            // the app icon that appeared and went away while a menu item was dragged.
            // A tool window is never given one.
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;

            return cp;
        }
    }
}
