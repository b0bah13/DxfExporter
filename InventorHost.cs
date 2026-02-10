using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Inventor;

namespace DxfExporter;

/// <summary>
/// Класс для работы с Inventor. Запуск и работа с ним в отдельном потоке.
/// </summary>
public sealed class InventorHost : IDisposable
{
    private Thread _thread;
    private Application _invApp;
    private readonly BlockingCollection<Func<Task>> _queue = new();
    private readonly ManualResetEventSlim _readyEvent = new(false);
    private volatile bool _isRunning = true; // volatile для потокобезопасности
    private bool _isInitialized = false;

    //public static InventorHost Instance { get; } = new InventorHost();
    // Ленивая инициализация Singleton
    public static readonly Lazy<InventorHost> Instance = new Lazy<InventorHost>(() => new InventorHost());

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public bool IsInitialized => _isInitialized;
    
    // Конструктор: создаёт STA-поток и запускает цикл обработки
    private InventorHost()
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "Inventor STA Thread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _readyEvent.Wait(); // ждём, пока Inventor инициализируется
        _isInitialized = true; // Помечаем, что инициализация прошла
    }

    private void ThreadProc()
    {
        try
        {
            // Попытка подключиться к запущенному приложению Inventor
            // CLSID Inventor.Application
            Guid clsid = new Guid("B6B5DC40-96E3-11d2-B774-0060B0F159EF");
            // Получаем активный экземпляр Inventor
            GetActiveObject(ref clsid, IntPtr.Zero, out object comObject);
            _invApp = (Application)comObject;
            _invApp.Visible = true;

            // Создаём Inventor в STA
            //Type invType = Type.GetTypeFromProgID("Inventor.Application");
            //_invApp = (Application)Activator.CreateInstance(invType);
            //_invApp.SilentOperation = true;
            //_invApp.Visible = false;
            _readyEvent.Set();

            // Цикл обработки команд
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                if (!_isRunning) break;
                action().Wait();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InventorHost exception: {ex.Message}");
        }
        finally
        {
            Cleanup();
        }
    }
    
    public Task RunAsync(Func<Application, Task> action)
    {
        var tcs = new TaskCompletionSource<object>();
        _queue.Add(async () =>
        {
            try
            {
                await action(_invApp);
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task<TResult> RunAsync<TResult>(Func<Application, Task<TResult>> action)
    {
        var tcs = new TaskCompletionSource<TResult>();

        _queue.Add(async () =>
        {
            try
            {
                var result = await action(_invApp);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Выполняет действие в STA-потоке Inventor без ожидания результата
    /// </summary>
    public void Run(Action<Application> action)
    {
        _queue.Add(() =>
        {
            action(_invApp);
            return Task.CompletedTask;
        });
    }


    private void Cleanup()
    {
        try
        {
            // Очистка событий и буфера iLogic
            _invApp.CommandManager.ClearPrivateEvents();
            _invApp.CommandManager.ControlDefinitions["iLogic.FreeILogicMemory"].Execute();

            // Освобождение коллекции документов
            foreach (Document oDoc in _invApp.Documents) { oDoc.ReleaseReference(); }
            _invApp.Documents.CloseAll(true);

            Marshal.ReleaseComObject(_invApp.Documents);
            
            Marshal.FinalReleaseComObject(_invApp);
            _invApp = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void Dispose()
    {
        /*
    _isRunning = false;
    // Останавливаем очередь и поток
    _queue.CompleteAdding();


    _thread.Join();

    Cleanup();

    */
        
        try
        {
            if (!_isRunning) return; // Защита от повторного вызова Dispose

            _isRunning = false;
            
            // Завершаем очередь
            _queue.CompleteAdding();
            
            /*
            // Добавляем в очередь задачу на финализацию (чтобы Inventor закрылся в своём STA)
            var tcs = new TaskCompletionSource<bool>();
            _queue.Add(() =>
            {
                try
                {
                    //Cleanup();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return Task.CompletedTask;
            });

            // Дожидаемся выполнения
            tcs.Task.Wait();
            */

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Dispose error: {ex.Message}");
            //_thread.Join(5000);
        }
        finally
        {
            _queue.Dispose();
            _thread = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

    }

}