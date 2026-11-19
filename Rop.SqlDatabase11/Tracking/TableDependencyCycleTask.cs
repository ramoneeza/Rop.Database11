using Microsoft.Extensions.Logging;

namespace Rop.Database11.Tracking;
internal class TableDependencyCycleTaskQueue: CycleTask.CycleTaskService<string,SqlTableDependency>
{
    public TableDependencyCycleTaskQueue(ILogger logger) : base(logger)
    {
    }
}
