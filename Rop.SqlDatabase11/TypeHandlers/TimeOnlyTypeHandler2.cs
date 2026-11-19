using System.Data;

namespace Rop.Database11.TypeHandlers
{
    /// <summary>
    /// Dapper type handler that maps SQL Server time values to nullable <see cref="TimeOnly"/>.
    /// </summary>
    public class TimeOnlyTypeHandler2 : SqlMapper.TypeHandler<TimeOnly?>
    {
        /// <summary>
        /// Converts a database value to a nullable <see cref="TimeOnly"/>.
        /// </summary>
        /// <param name="value">Database value, expected to be a <see cref="TimeSpan"/> or null.</param>
        /// <returns>The time part of the value, or null when the value is null.</returns>
        public override TimeOnly? Parse(object? value)
        {
            return value is null ? null : TimeOnly.FromTimeSpan((TimeSpan)value);
        }

        /// <summary>
        /// Assigns the nullable <see cref="TimeOnly"/> value to the parameter as a <see cref="TimeSpan"/> or null.
        /// </summary>
        /// <param name="parameter">Database parameter to set.</param>
        /// <param name="value">Value to assign.</param>
        public override void SetValue(IDbDataParameter parameter, TimeOnly? value)
        {
            parameter.Value = value?.ToTimeSpan();
        }
    }
}