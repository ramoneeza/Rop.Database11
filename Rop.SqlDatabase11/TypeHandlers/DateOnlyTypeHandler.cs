using System.Data;

namespace Rop.Database11.TypeHandlers
{
    /// <summary>
    /// Dapper type handler that maps SQL Server date values to <see cref="DateOnly"/>.
    /// </summary>
    public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        /// <summary>
        /// Converts a database value to a <see cref="DateOnly"/>.
        /// </summary>
        /// <param name="value">Database value, expected to be a <see cref="DateTime"/>.</param>
        /// <returns>The date part of the value.</returns>
        public override DateOnly Parse(object value)
        {
            return DateOnly.FromDateTime((DateTime)value);
        }

        /// <summary>
        /// Assigns the <see cref="DateOnly"/> value to the parameter as a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="parameter">Database parameter to set.</param>
        /// <param name="value">Value to assign.</param>
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }
}