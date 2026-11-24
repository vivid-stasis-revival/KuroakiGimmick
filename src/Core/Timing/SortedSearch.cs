namespace KuroakiGimmick.Core;

/// <summary>已按 key 升序排列序列的二分下界；返回首个 key ≥ value 的索引。</summary>
public static class SortedSearch
{
    /// <summary>
    /// 调用方必须保证 key 沿序列单调不减；未排序时不会报错，只会返回无意义的位置。
    /// 全部元素都小于 value 时返回 values.Count，调用方需自行判边界。
    /// </summary>
    /// <param name="values">按键升序排列的元素。</param>
    /// <param name="value">查找下界。</param>
    /// <param name="key">提取排序键的函数。</param>
    /// <returns>首个键不小于给定值的索引，未命中时为元素数。</returns>
    /// <typeparam name="T">已排序序列的元素类型。</typeparam>
    public static int LowerBound<T>(IReadOnlyList<T> values, double value, Func<T, double> key)
    {
        int low = 0, high = values.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (key(values[middle]) < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    /// <summary>
    /// 同一序列的二分上界；返回首个 key &gt; value 的索引，也就是 key ≤ value 的元素个数。
    /// 与 LowerBound 的区别只在相等时往哪边收：查"到此刻为止发生了几次"必须用这个，用 LowerBound 会漏掉正好落在 value 上的那一次。
    /// </summary>
    /// <param name="values">按键升序排列的元素。</param>
    /// <param name="value">查找上界。</param>
    /// <param name="key">提取排序键的函数。</param>
    /// <returns>首个键大于给定值的索引，未命中时为元素数。</returns>
    /// <typeparam name="T">已排序序列的元素类型。</typeparam>
    public static int UpperBound<T>(IReadOnlyList<T> values, double value, Func<T, double> key)
    {
        int low = 0, high = values.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (key(values[middle]) <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }
}

