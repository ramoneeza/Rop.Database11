namespace Rop.Database11
{
    /// <summary>
    /// Helper Class for Linq. OrderBy and ThenBy with Descending option
    /// </summary>
    public static class LinqHelper
    {
        /// <summary>
        /// Orders the elements of a sequence by the given key selector in ascending or descending order.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <typeparam name="K">Key type</typeparam>
        /// <param name="lst">Sequence to order.</param>
        /// <param name="fnorder">Key selector.</param>
        /// <param name="desc">When true, sorts in descending order; otherwise ascending.</param>
        /// <returns>Ordered sequence.</returns>
        public static IOrderedEnumerable<T> OrderBy<T, K>(this IEnumerable<T> lst, Func<T, K> fnorder, bool desc)
        {
            if (!desc)
                return lst.OrderBy(fnorder);
            else
                return lst.OrderByDescending(fnorder);
        }
        /// <summary>
        /// Performs a subsequent ordering of an ordered sequence by the given key selector in ascending or descending order.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <typeparam name="K">Key type</typeparam>
        /// <param name="lst">Ordered sequence.</param>
        /// <param name="fnorder">Key selector.</param>
        /// <param name="desc">When true, sorts in descending order; otherwise ascending.</param>
        /// <returns>Ordered sequence.</returns>
        public static IOrderedEnumerable<T> ThenBy<T, K>(this IOrderedEnumerable<T> lst, Func<T, K> fnorder, bool desc)
        {
            if (!desc)
                return lst.ThenBy(fnorder);
            else
                return lst.ThenByDescending(fnorder);
        }

    }
}
