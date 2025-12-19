using System;
using System.Threading;
using System.Threading.Tasks;
using TornadoTest2.Controllers;

// Демонстрация простого контроллера асинхронно-синхронного ввода, принимающего блоки фиксированного размера из симулируемого callback
Console.WriteLine("Запуск демонстрации...");

var controller = new AsyncSyncController(blockSize: 8, capacityBlocks: 64);

// Симуляция асинхронного продюсера через фоновую задачу, имитирующую прерывание/callback
var cts = new CancellationTokenSource();
var producerTask = Task.Run(async () =>
{
    var rnd = new Random();
    while (!cts.Token.IsCancellationRequested)
    {
        // генерируем блок фиксированного размера
        var block = new byte[8];
        rnd.NextBytes(block);
        // В реальном прерывании/callback этот вызов должен быть очень быстрым
        controller.OnDataReceived(block);
        await Task.Delay(50, cts.Token);
    }
}, cts.Token);

// Консюмер: синхронно читает блоки
for (int i = 0; i < 10; i++)
{
    try
    {
        var data = controller.ReadBlock(timeoutMs: 1000);
        Console.WriteLine($"Прочитан блок #{i}: {BitConverter.ToString(data)}");
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Время ожидания истекло");
    }
}

// останавливаем продюсера
cts.Cancel();
try { await producerTask; } catch {}

Console.WriteLine("Демонстрация завершена");
