using System.Collections.Generic;

namespace Sytem;

public static class ListExt
{
    public static bool IsInBounds<T>(this IReadOnlyList<T> list, int index)
    {
        if (0 <= index && index < list.Count) return true;
        else return false;
    }
}
