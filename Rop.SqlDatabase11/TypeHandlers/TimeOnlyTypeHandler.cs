using System.Data;

namespace Rop.Database11.TypeHandlers
{
    /// <summary>
    /// Dapper type handler that maps SQL Server time values to <see cref="TimeOnly"/>.
    /// </summary>
    public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        /// <summary>
        /// Converts a database value to a <see cref="TimeOnly"/>.
        /// </summary>
        /// <param name="value">Database value, expected to be a <see cref="TimeSpan"/>.</param>
        /// <returns>The time part of the value.</returns>
        public override TimeOnly Parse(object value)
        {
            return TimeOnly.FromTimeSpan((TimeSpan)value);
        }

        /// <summary>
        /// Assigns the <see cref="TimeOnly"/> value to the parameter as a <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="parameter">Database parameter to set.</param>
        /// <param name="value">Value to assign.</param>
        public override void SetValue(IDbDataParameter parameter, TimeOnly value)
        {
            parameter.Value = value.ToTimeSpan();
        }
    }
}
