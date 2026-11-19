using Rop.Database11.CacheRepository;
using Rop.Results9;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for MemoryCache (SD-2, SD-6): Expires must ignore missing keys
/// instead of throwing KeyNotFoundException; the cleanup event must be
/// resilient to faulty handlers and the cache must be disposable.
/// </summary>
public class MemoryCacheTests
{
    private static MemoryCache<int, ItemIntAuto> NewCache() => new(
        item => item.Id,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(1), // intervalo de limpieza largo para que el timer no interfiera en los tests
        null);

    [Fact]
    public void Expires_With_Missing_Keys_Does_Not_Throw()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });
        cache.Add(new ItemIntAuto { Id = 2, Name = "B", Quantity = 2 });

        // La clave 3 no existe: reproduce los INSERTs del change tracking
        // que reportan claves que aún no están en el cache.
        var expired = cache.Expires(new[] { 1, 3 });

        Assert.Equal(new[] { 1 }, expired);
        var item1 = cache.GetValueOrDefault(1);
        Assert.True(item1 is ExpiredItem);
        Assert.Null(cache.GetValueOrDefault(3));
        Assert.True(cache.GetValueOrDefault(2) is ValidItem<ItemIntAuto>);
    }

    [Fact]
    public void Expires_With_Empty_List_Returns_Empty()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        var expired = cache.Expires();

        Assert.Empty(expired);
        Assert.True(cache.GetValueOrDefault(1) is ValidItem<ItemIntAuto>);
    }

    [Fact]
    public void Expires_Marks_All_Existing_Keys_As_Expired()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });
        cache.Add(new ItemIntAuto { Id = 2, Name = "B", Quantity = 2 });

        var expired = cache.Expires(new[] { 1, 2 });

        Assert.Equal(new[] { 1, 2 }, expired);
        Assert.True(cache.GetValueOrDefault(1) is ExpiredItem);
        Assert.True(cache.GetValueOrDefault(2) is ExpiredItem);
    }

    // U-2: TryGetValue devuelve el estado como union y transiciona al expirar:
    // ValidItem -> JustExpiredItem (primer acceso tras expirar) -> ExpiredItem.
    [Fact]
    public void TryGetValue_Returns_States_And_Transitions_On_Expiry()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(50));

        Assert.True(cache.TryGetValue(1) is ValidItem<ItemIntAuto>);

        Thread.Sleep(150); // deja pasar la expiracion

        Assert.True(cache.TryGetValue(1) is JustExpiredItem);
        Assert.True(cache.TryGetValue(1) is ExpiredItem);

        Assert.Null(cache.TryGetValue(99)); // clave inexistente
    }

    // SD-6: un handler que lanza no debe impedir que los demás handlers se ejecuten
    // ni propagar la excepción (callback de Timer en hilo de fondo = caída del proceso).
    [Fact]
    public void OnCacheCleanUp_Handler_Throwing_Does_Not_Stop_Other_Handlers()
    {
        using var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        var secondHandlerKeys = new List<int>();
        cache.CacheCleanUp += (_, _) => throw new InvalidOperationException("handler roto");
        cache.CacheCleanUp += (_, args) => secondHandlerKeys.AddRange(args.CleanUpKeys.Cast<int>());

        // Expires es el camino público que dispara CacheCleanUp sin depender del timer.
        cache.Expires(new[] { 1 });

        Assert.Equal(new[] { 1 }, secondHandlerKeys);
    }

    // SD-6: Dispose no lanza y es idempotente.
    [Fact]
    public void Dispose_Does_Not_Throw_And_Is_Idempotent()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        cache.Dispose();
        cache.Dispose();
    }

    // SD-6: tras Dispose el timer deja de disparar el evento de limpieza.
    [Fact]
    public void Dispose_Stops_Timer()
    {
        var cache = new MemoryCache<int, ItemIntAuto>(
            item => item.Id,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMilliseconds(10),
            null);

        var cleanupCount = 0;
        // El handler realimenta el cache con un item de vida corta para que el timer
        // tenga siempre algo expirado que limpiar (si no, tras la primera expiración
        // el evento dejaría de dispararse y el test no distinguiría nada).
        cache.CacheCleanUp += (_, _) =>
        {
            Interlocked.Increment(ref cleanupCount);
            cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(20));
        };
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(20));

        // Espera generosa a que el timer dispare al menos un par de veces.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref cleanupCount) < 2 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
        Assert.True(Volatile.Read(ref cleanupCount) >= 2, "el timer debería haber disparado la limpieza antes del Dispose");

        cache.Dispose();

        // Margen para que termine un tick que ya estuviera en vuelo, y entonces se congela el contador.
        Thread.Sleep(200);
        var frozenCount = Volatile.Read(ref cleanupCount);
        Thread.Sleep(300);
        Assert.Equal(frozenCount, Volatile.Read(ref cleanupCount));
    }

    // SD-4: la consulta a BD (callback de RefreshAll) no debe ejecutarse bajo el lock.
    [Fact]
    public void RefreshAll_Does_Not_Hold_Lock_During_Refresh_Callback()
    {
        var cache = NewCache();
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);

        var refresh = Task.Run(() => cache.RefreshAll((_, _) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new EnumerableResult<ItemIntAuto>(new List<ItemIntAuto>());
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        // Mientras la recarga esta bloqueada, escribir en el cache debe completar.
        var addTask = Task.Run(() => cache.Add(new ItemIntAuto { Id = 9, Name = "X", Quantity = 1 }));
        Assert.True(addTask.Wait(TimeSpan.FromSeconds(2))); // antes quedaba bloqueado por el lock

        release.Set();
        Assert.True(refresh.Wait(TimeSpan.FromSeconds(5)));
        var name = cache.GetValueOrDefault(9) switch
        {
            ValidItem<ItemIntAuto> vi => vi.Value.Name,
            _ => throw new InvalidOperationException("se esperaba un item valido")
        };
        Assert.Equal("X", name);
    }
}
