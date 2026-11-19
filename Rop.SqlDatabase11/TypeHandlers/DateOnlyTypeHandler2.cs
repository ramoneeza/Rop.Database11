using System.Data;

namespace Rop.Database11.TypeHandlers
{
    /// <summary>
    /// Dapper type handler that maps SQL Server date values to nullable <see cref="DateOnly"/>.
    /// </summary>
    public class DateOnlyTypeHandler2 : SqlMapper.TypeHandler<DateOnly?>
    {
        /// <summary>
        /// Converts a database value to a nullable <see cref="DateOnly"/>.
        /// </summary>
        /// <param name="value">Database value, expected to be a <see cref="DateTime"/> or null.</param>
        /// <returns>The date part of the value, or null when the value is null.</returns>
        public override DateOnly? Parse(object? value)
        {
            return value is null ? null : DateOnly.FromDateTime((DateTime)value);
        }

        /// <summary>
        /// Assigns the nullable <see cref="DateOnly"/> value to the parameter as a <see cref="DateTime"/> or null.
        /// </summary>
        /// <param name="parameter">Database parameter to set.</param>
        /// <param name="value">Value to assign.</param>
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.Value = value?.ToDateTime(TimeOnly.MinValue);
        }
    }
}