namespace TaskbarNetMonitor.Core;

/// <summary>
/// Integer screen rectangle. Kept separate from <see cref="System.Drawing.Rectangle"/>
/// so the placement maths can be unit tested without pulling in any UI type.
/// </summary>
internal readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom)
        => new(left, top, right - left, bottom - top);

    public bool IntersectsWith(PixelRect other)
        => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        if (right <= left || bottom <= top)
        {
            return default;
        }

        return FromEdges(left, top, right, bottom);
    }

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}
