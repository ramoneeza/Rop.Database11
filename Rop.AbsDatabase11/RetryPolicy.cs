namespace Rop.Database;

/// <summary>
/// Política de reintentos ante errores transitorios de la base de datos
/// (deadlocks 1205, timeouts -2, fallos de conexión/red, errores transitorios de Azure SQL...).
/// </summary>
/// <remarks>
/// Los reintentos ejecutan la operación completa con una conexión NUEVA (y, en las rutas
/// transaccionales, una transacción nueva). Los errores de negocio devueltos como
/// <c>Result</c> fallido NO se reintentan: solo se reintentan las excepciones transitorias.
/// Los reintentos se aplican a todas las operaciones de <see cref="AbsDatabase"/>
/// (UnitOfWork, Query, Execute, Insert/Update/Delete...) salvo a las consultas que abren
/// su propia conexión fuera de esa capa (p. ej. el polling del change tracking, que se
/// autocura en el siguiente ciclo).
/// </remarks>
public sealed record RetryPolicy
{
    /// <summary>
    /// Política por defecto: 3 reintentos con backoff de 100, 250 y 500 ms y
    /// detección de los errores transitorios estándar de SQL Server.
    /// </summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>
    /// Política sin reintentos (un único intento).
    /// </summary>
    public static RetryPolicy NoRetry { get; } = new() { MaxRetries = 0 };

    /// <summary>
    /// Número máximo de reintentos tras el primer intento (0 = sin reintentos). Por defecto 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Retardo antes de cada reintento (uno por reintento, en orden).
    /// Si hay más reintentos que retardos se reutiliza el último. Por defecto 100, 250 y 500 ms.
    /// </summary>
    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];

    /// <summary>
    /// Predicado que decide si una excepción es transitoria y merece reintento.
    /// Si es <c>null</c> se usa el detector estándar (<see cref="IsTransientDefault"/>).
    /// </summary>
    public Func<Exception, bool>? IsTransient { get; init; }

    /// <summary>
    /// Clasifica una excepción como transitoria (sin tener en cuenta el número de intentos).
    /// </summary>
    internal bool IsTransientError(Exception ex) => IsTransient is not null ? IsTransient(ex) : IsTransientDefault(ex);

    /// <summary>
    /// Devuelve si el intento fallido número <paramref name="attempt"/> (1-based)
    /// merece reintento con esta política.
    /// </summary>
    /// <param name="ex">Excepción del intento fallido.</param>
    /// <param name="attempt">Número de intento fallido (1 = el primero).</param>
    internal bool CanRetry(Exception ex, int attempt)
    {
        if (attempt > MaxRetries) return false;
        return IsTransientError(ex);
    }

    /// <summary>
    /// Retardo a aplicar tras el intento fallido número <paramref name="attempt"/>.
    /// </summary>
    internal TimeSpan DelayFor(int attempt)
    {
        if (Delays.Count == 0) return TimeSpan.Zero;
        return Delays[Math.Min(attempt - 1, Delays.Count - 1)];
    }

    /// <summary>
    /// Detector estándar de errores transitorios de SQL Server (funciona sin referenciar
    /// Microsoft.Data.SqlClient: detecta <c>SqlException</c> por reflexión).
    /// </summary>
    /// <param name="ex">Excepción a clasificar.</param>
    /// <returns><c>true</c> si la excepción es un error transitorio reintentable.</returns>
    public static bool IsTransientDefault(Exception ex)
    {
        if (ex is TimeoutException) return true;
        if (ex is AggregateException ag) return ag.InnerExceptions.Any(IsTransientDefault);
        var type = ex.GetType();
        var name = type.FullName;
        if (name is not ("Microsoft.Data.SqlClient.SqlException" or "System.Data.SqlClient.SqlException")) return false;
        var numberProp = type.GetProperty("Number");
        var number = numberProp?.GetValue(ex) is int n ? n : 0;
        // 1205 deadlock | -2 timeout | 2/53/64/20/233 conexión | 10053/10054/10060 winsock |
        // 4060 BD no disponible | 40197/40501/40540/40613/10928/10929/40627/4221/615 Azure SQL
        if (number is 1205 or -2 or 2 or 53 or 64 or 20 or 233 or 4060
            or 10053 or 10054 or 10060
            or 40197 or 40501 or 40540 or 40613 or 10928 or 10929 or 40627 or 4221 or 615) return true;
        var isTransientProp = type.GetProperty("IsTransient");
        return isTransientProp?.GetValue(ex) is true;
    }
}
